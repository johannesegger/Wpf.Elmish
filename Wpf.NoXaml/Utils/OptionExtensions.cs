using LanguageExt;
using static LanguageExt.Prelude;

namespace Wpf.NoXaml.Utils
{
    public static class OptionExtensions
    {
        public static Option<T> OfType<T>(this Option<object> o)
        {
            return o.Bind(p => p is T q ? Some(q) : None);
        }
    }
}