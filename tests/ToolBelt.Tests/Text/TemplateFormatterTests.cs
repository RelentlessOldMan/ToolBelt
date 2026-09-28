using System;
using System.Collections.Generic;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class TemplateFormatterTests
    {
        private static readonly Dictionary<string, string> Values = new Dictionary<string, string>
        {
            ["name"] = "Ada",
            ["lang"] = "C#",
        };

        public void SubstitutesPlaceholders()
        {
            Check.Equal("Hi Ada, welcome to C#.",
                TemplateFormatter.Format("Hi {name}, welcome to {lang}.", Values));
        }

        public void TrimsPlaceholderNames()
        {
            Check.Equal("Ada", TemplateFormatter.Format("{  name }", Values));
        }

        public void EscapedBraces()
        {
            Check.Equal("{name} = Ada", TemplateFormatter.Format("{{name}} = {name}", Values));
            Check.Equal("{}", TemplateFormatter.Format("{{}}", Values));
        }

        public void NoPlaceholders_Unchanged()
        {
            Check.Equal("plain text", TemplateFormatter.Format("plain text", Values));
            Check.Equal("", TemplateFormatter.Format("", Values));
        }

        public void ResolverOverload_SuppliesDefaults()
        {
            string result = TemplateFormatter.Format(
                "{a}-{b}", name => name == "a" ? "1" : "?");
            Check.Equal("1-?", result);
        }

        public void UnknownPlaceholder_Throws()
        {
            Check.Throws<KeyNotFoundException>(() => TemplateFormatter.Format("{missing}", Values));
        }

        public void MalformedTemplate_Throws()
        {
            Check.Throws<FormatException>(() => TemplateFormatter.Format("{unclosed", Values));
            Check.Throws<FormatException>(() => TemplateFormatter.Format("{}", Values));       // empty
            Check.Throws<FormatException>(() => TemplateFormatter.Format("a } b", Values));    // stray close
        }

        public void NullArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => TemplateFormatter.Format(null!, Values));
            Check.Throws<ArgumentNullException>(() => TemplateFormatter.Format("x", (IReadOnlyDictionary<string, string>)null!));
            Check.Throws<ArgumentNullException>(() => TemplateFormatter.Format("x", (Func<string, string>)null!));
        }
    }
}
