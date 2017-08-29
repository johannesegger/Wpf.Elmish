using System;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Expression = System.Linq.Expressions.Expression;

namespace Wpf.NoXaml.Utils
{
    public static class ExpressionExtensions
    {
        public static Action<TObj, TProp> CreateSetter<TObj, TProp>(
            this Expression<Func<TObj, TProp>> getter)
        {
            var memberExpression = (MemberExpression)getter.Body;
            var property = (PropertyInfo)memberExpression.Member;
            var setMethod = property.GetSetMethod();

            var parameterValue = Expression.Parameter(typeof(TProp), "value");

            var newExpression = Expression.Lambda<Action<TObj, TProp>>(
                Expression.Call(memberExpression.Expression, setMethod, parameterValue),
                getter.Parameters.Concat(new[] { parameterValue }));
            return newExpression.Compile();
        }

        // TODO create nice exception when name of ctor parameter doesn't match property name
        public static Func<T, TProp, T> CreateImmutableSetter<T, TProp>(this Expression<Func<T, TProp>> propertyExpression)
        {
            var objectParameter = propertyExpression.Parameters.Single();
            var valueParameter = Expression.Parameter(typeof(TProp), "value");
            var p = new { expr = propertyExpression.Body, value = (Expression)valueParameter };
            while (p.expr != objectParameter)
            {
                if (p.expr.NodeType == ExpressionType.MemberAccess)
                {
                    var expr = (MemberExpression)p.expr;
                    var parent = expr.Expression;

                    var ctor = parent.Type.GetConstructors().Single(c => c.IsPublic && !c.IsStatic);
                    var arguments = ctor
                        .GetParameters()
                        .Select(parameter => parent.Type.GetProperty(
                            parameter.Name.FirstToUpper(),
                            BindingFlags.Public | BindingFlags.Instance))
                        .Select(prop => prop.Name.Equals(expr.Member.Name)
                            ? p.value
                            : Expression.Property(parent, prop))
                        .ToList();

                    p = new
                    {
                        expr = parent,
                        value = (Expression)Expression.New(ctor, arguments)
                    };
                }
                else if (TryConvertToIndexExpession(p.expr, out IndexExpression expr))
                {
                    var parent = expr.Object;

                    var isImmutableList = parent.Type
                        .GetInterfaces()
                        .Concat(new[] { parent.Type })
                        .Where(o => o.IsGenericType)
                        .Select(o => o.GetGenericTypeDefinition())
                        .Contains(typeof(IImmutableList<>));
                    if (!isImmutableList)
                    {
                        throw new Exception($"Expected collection to be of type {typeof(IImmutableList<>).Name}.");
                    }

                    var indexExpression = expr.Arguments.Single();

                    var setItemExpression = Expression.Call(
                        parent,
                        parent.Type.GetMethod(nameof(IImmutableList<object>.SetItem), BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy),
                        indexExpression,
                        p.value);

                    p = new
                    {
                        expr = parent,
                        value = (Expression)setItemExpression
                    };
                }
                else
                {
                    throw new NotSupportedException($"Can't process expression {p.expr} (node type {p.expr.NodeType}).");
                }
            }

            return Expression
                .Lambda<Func<T, TProp, T>>(p.value, propertyExpression.Parameters.Single(), valueParameter)
                .Compile();
        }

        private static bool TryConvertToIndexExpession(Expression expr, out IndexExpression result)
        {
            if (expr.NodeType == ExpressionType.Index)
            {
                result = (IndexExpression)expr;
                return true;
            }

            if (expr.NodeType == ExpressionType.Call)
            {
                var callExpr = (MethodCallExpression)expr;
                var indexProperty = callExpr.Method.DeclaringType.GetProperties()
                    .FirstOrDefault(p => p.GetMethod == callExpr.Method);
                if (indexProperty != null)
                {
                    result = Expression.MakeIndex(callExpr.Object, indexProperty, callExpr.Arguments);
                    return true;
                }
            }

            result = null;
            return false;
        }

        public static T Set<T, TProp>(this T root, Expression<Func<T, TProp>> expr, TProp value)
        {
            return expr.CreateImmutableSetter()(root, value);
        }

        public static T Set<T, TProp>(this T root, Expression<Func<T, TProp>> expr, Func<T, TProp> fn)
        {
            return expr.CreateImmutableSetter()(root, fn(root));
        }
    }
}