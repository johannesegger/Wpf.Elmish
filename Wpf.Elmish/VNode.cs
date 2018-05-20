﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Disposables;
using System.Windows;
using LanguageExt;
using Wpf.Elmish.Utils;
using static LanguageExt.Prelude;

namespace Wpf.Elmish
{
    public interface IVNode
    {
        IResourceDisposable Materialize(Option<object> node);
    }

    public interface IVNode<T> : IVNode
    {
        IResourceDisposable<T> Materialize(Option<T> node);
    }

    public class VNode<T> : IVNode<T>
    {
        private readonly Func<Option<T>, IResourceDisposable<T>> _createOrUpdate;

        public VNode(Func<Option<T>, IResourceDisposable<T>> createOrUpdate)
        {
            _createOrUpdate = createOrUpdate;
        }

        IResourceDisposable IVNode.Materialize(Option<object> node) => Materialize(node.TryCast<T>());
        public IResourceDisposable<T> Materialize(Option<T> node) => _createOrUpdate(node);
    }

    public static class VNode
    {
        public static IVNode<T> Create<T>()
            where T : DependencyObject, new()
        {
            return new VNode<T>(node => ResourceDisposable.Create(node.IfNone(() => new T())));
        }

        public static IVNode<TBase> Create<TBase, TImpl>()
            where TBase : DependencyObject
            where TImpl : TBase, new()
        {
            return new VNode<TBase>(node => ResourceDisposable.Create(node.TryCast<TImpl>().IfNone(() => new TImpl())));
        }
    }

    public static class VNodeExtensions
    {
        private static IVNode<T> Set<T, TProp>(
            this IVNode<T> vNode,
            Expression<Func<T, TProp>> propertyExpression,
            Func<Option<TProp>, IResourceDisposable<TProp>> getValue,
            IEqualityComparer<TProp> equalityComparer)
        {
            var getter = propertyExpression.Compile();
            var setter = propertyExpression.CreateSetter();
            return new VNode<T>(node =>
            {
                var o = vNode.Materialize(node);
                var existingValue = getter(o.Resource);
                var value = getValue(existingValue);
                if (!equalityComparer.Equals(value.Resource, existingValue))
                {
                    setter(o.Resource, value.Resource);
                }
                return o.AddDisposable(value);
            });
        }

        public static IVNode<T> Set<T, TProp>(
            this IVNode<T> node,
            Expression<Func<T, TProp>> propertyExpression,
            IVNode<TProp> value)
        {
            return node.Set(
                propertyExpression,
                o => value.Materialize(o),
                EqualityComparer<TProp>.Default);
        }

        public static IVNode<T> Set<T, TProp>(
            this IVNode<T> node,
            Expression<Func<T, TProp>> propertyExpression,
            TProp value,
            IEqualityComparer<TProp> equalityComparer)
        {
            return node.Set(
                propertyExpression,
                _ => ResourceDisposable.Create(value),
                equalityComparer);
        }

        public static IVNode<T> Set<T, TProp>(
            this IVNode<T> node,
            Expression<Func<T, TProp>> propertyExpression,
            TProp value)
        {
            return node.Set(
                propertyExpression,
                value,
                EqualityComparer<TProp>.Default);
        }

        public static IVNode<T> SetCollection<T>(
            this IVNode<T> vNode,
            Expression<Func<T, IList>> propertyExpression,
            IEnumerable<IVNode> children)
        {
            var getter = propertyExpression.Compile();
            return new VNode<T>(node =>
            {
                var oldCollection = node
                    .Some(getter)
                    .None(new List<object>());

                var o = vNode.Materialize(node);
                var newCollection = getter(o.Resource);
                var materializedChildren = new CompositeDisposable();
                var i = 0;
                foreach (var child in children)
                {
                    var oldItem = oldCollection.Count > i ? Some(oldCollection[i]) : None;

                    var newItem = child
                        .Materialize(oldItem)
                        .DisposeWith(materializedChildren)
                        .Resource;

                    if (ReferenceEquals(oldCollection, newCollection))
                    {
                        oldItem
                            .Match(
                                p =>
                                {
                                    if (!ReferenceEquals(p, newItem))
                                    {
                                        // WPF doesn't allow updating using the indexer
                                        newCollection.RemoveAt(i);
                                        newCollection.Insert(i, newItem);
                                    }
                                },
                                () => newCollection.Add(newItem));
                    }
                    else
                    {
                        newCollection.Add(newItem);
                    }
                    i++;
                }

                for (var j = oldCollection.Count - 1; j >= i; j--)
                {
                    oldCollection.RemoveAt(j);
                }

                return o.AddDisposable(materializedChildren);
            });
        }

        public static IVNode<T> SetCollection<T>(
            this IVNode<T> node,
            Expression<Func<T, IList>> propertyExpression,
            params IVNode[] children)
        {
            return node.SetCollection(propertyExpression, children.AsEnumerable());
        }

        public static IVNode<T> SetCollection<T>(
            this IVNode<T> vNode,
            Expression<Func<T, IEnumerable>> propertyExpression,
            IEnumerable<object> children)
        {
            var getter = propertyExpression.Compile();
            var setter = propertyExpression.CreateSetter();
            if (setter == null)
            {
                throw new Exception($"Setter must not be null. Type: {typeof(T).FullName}, Property: {propertyExpression}");
            }

            return new VNode<T>(node =>
            {
                var o = vNode.Materialize(node);
                var newCollection = getter(o.Resource);
                if (newCollection == null || !newCollection.Cast<object>().SequenceEqual(children))
                {
                    setter(o.Resource, children);
                }
                return o;
            });
        }

        public static IVNode<T> SetCollection<T>(
            this IVNode<T> node,
            Expression<Func<T, IEnumerable>> propertyExpression,
            params object[] children)
        {
            return node.SetCollection(propertyExpression, children.AsEnumerable());
        }

        public static IVNode<T> SetCollection<T>(
            this IVNode<T> vNode,
            Expression<Func<T, IList>> propertyExpression,
            IEnumerable<object> children)
        {
            var getter = propertyExpression.Compile();
            var setter = propertyExpression.CreateSetter();
            return new VNode<T>(node =>
            {
                var o = vNode.Materialize(node);
                var newCollection = getter(o.Resource);
                var i = 0;
                foreach (var child in children)
                {
                    if (newCollection.Count <= i)
                    {
                        newCollection.Add(child);
                    }
                    else if (!Equals(newCollection[i], child))
                    {
                        newCollection[i] = child;
                    }
                    i++;
                }

                return o;
            });
        }

        public static IVNode<T> SetCollection<T>(
            this IVNode<T> node,
            Expression<Func<T, IList>> propertyExpression,
            params object[] children)
        {
            return node.SetCollection(propertyExpression, children.AsEnumerable());
        }

        public static IVNode<T> Subscribe<T>(
            this IVNode<T> vNode,
            Func<T, IDisposable> fn)
        {
            return new VNode<T>(node =>
            {
                var o = vNode.Materialize(node);
                var subscription = fn(o.Resource);
                return o.AddDisposable(subscription);
            });
        }

        public static IVNode<T> Attach<T, TProp>(
            this IVNode<T> vNode,
            DependencyProperty dependencyProperty,
            TProp value)
            where T : DependencyObject
        {
            return new VNode<T>(node =>
            {
                var o = vNode.Materialize(node);
                o.Resource.SetValue(dependencyProperty, value);
                return o;
            });
        }
    }
}
