using System;
using System.ComponentModel.Composition;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace BetterAsmHighlighter.Editor
{
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType(AsmContentTypeDefinition.CONTENT_TYPE_NAME)]
    [TextViewRole(PredefinedTextViewRoles.Interactive)]
    internal class AsmCompletionHandlerProvider : IVsTextViewCreationListener
    {
        [Import]
        internal IVsEditorAdaptersFactoryService AdapterService = null!;

        [Import]
        internal ICompletionBroker CompletionBroker = null!;

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            ITextView textView = AdapterService.GetWpfTextView(textViewAdapter);
            if (textView == null) return;

            Func<AsmCompletionCommandHandler> createCommandHandler = () => new AsmCompletionCommandHandler(textViewAdapter, textView, CompletionBroker);
            textView.Properties.GetOrCreateSingletonProperty(createCommandHandler);
        }
    }

    internal class AsmCompletionCommandHandler : IOleCommandTarget
    {
        private IOleCommandTarget _nextCommandHandler;
        private ITextView _textView;
        private ICompletionBroker _broker;
        private ICompletionSession? _currentSession;

        internal AsmCompletionCommandHandler(IVsTextView textViewAdapter, ITextView textView, ICompletionBroker broker)
        {
            _textView = textView;
            _broker = broker;

            // Add the command targeted to the filter chain
            textViewAdapter.AddCommandFilter(this, out _nextCommandHandler);
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _nextCommandHandler.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            char typedChar = char.MinValue;
            if (pguidCmdGroup == VSConstants.VSStd2K && nCmdID == (uint)VSConstants.VSStd2KCmdID.TYPECHAR)
            {
                typedChar = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
            }

            // Check for commit keys
            if (nCmdID == (uint)VSConstants.VSStd2KCmdID.RETURN
                || nCmdID == (uint)VSConstants.VSStd2KCmdID.TAB)
            {
                if (_currentSession != null && !_currentSession.IsDismissed)
                {
                    if (_currentSession.SelectedCompletionSet != null &&
                        _currentSession.SelectedCompletionSet.SelectionStatus.IsSelected)
                    {
                        _currentSession.Commit();
                        return VSConstants.S_OK;
                    }
                    else
                    {
                        _currentSession.Dismiss();
                    }
                }
            }

            // Pass through the command
            int retVal = _nextCommandHandler.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);

            // Handle manual trigger: Ctrl+J or Ctrl+Space
            if (pguidCmdGroup == VSConstants.VSStd2K && (nCmdID == (uint)VSConstants.VSStd2KCmdID.COMPLETEWORD || nCmdID == (uint)VSConstants.VSStd2KCmdID.AUTOCOMPLETE))
            {
                TriggerCompletion();
                return VSConstants.S_OK;
            }

            // Auto trigger on typing letters, digits, or '.', '%', '@', '_'
            if (!char.IsControl(typedChar))
            {
                if (char.IsLetterOrDigit(typedChar) || typedChar == '.' || typedChar == '%' || typedChar == '@' || typedChar == '_')
                {
                    if (_currentSession == null || _currentSession.IsDismissed)
                    {
                        TriggerCompletion();
                    }
                    else
                    {
                        _currentSession.Filter();
                    }
                }
                else if (_currentSession != null && !_currentSession.IsDismissed)
                {
                    _currentSession.Filter();
                }
            }

            return retVal;
        }

        private bool TriggerCompletion()
        {
            SnapshotPoint? caretPoint = _textView.Caret.Position.Point.GetPoint(
                textBuffer => (!textBuffer.ContentType.IsOfType("projection")), PositionAffinity.Predecessor);
            if (!caretPoint.HasValue)
            {
                return false;
            }

            _currentSession = _broker.CreateCompletionSession(_textView,
                caretPoint.Value.Snapshot.CreateTrackingPoint(caretPoint.Value.Position, PointTrackingMode.Positive),
                true);

            _currentSession.Dismissed += (sender, args) => _currentSession = null;
            _currentSession.Start();
            return true;
        }
    }
}
