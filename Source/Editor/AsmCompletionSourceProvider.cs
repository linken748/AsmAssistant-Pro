using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace BetterAsmHighlighter.Editor
{
    [Export(typeof(ICompletionSourceProvider))]
    [ContentType(AsmContentTypeDefinition.CONTENT_TYPE_NAME)]
    [Name("AsmCompletionSourceProvider")]
    internal class AsmCompletionSourceProvider : ICompletionSourceProvider
    {
        public ICompletionSource TryCreateCompletionSource(ITextBuffer textBuffer)
        {
            return new AsmCompletionSource(textBuffer);
        }
    }
}
