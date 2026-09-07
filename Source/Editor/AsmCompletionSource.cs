using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using BetterAsmHighlighter.Data;

namespace BetterAsmHighlighter.Editor
{
    internal class AsmCompletionSource : ICompletionSource
    {
        private readonly ITextBuffer _buffer;
        private bool _isDisposed;
        private static List<Completion>? _cachedStaticCompletions;

        public AsmCompletionSource(ITextBuffer buffer)
        {
            _buffer = buffer;
        }

        public void AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets)
        {
            if (_isDisposed) return;

            var snapshot = _buffer.CurrentSnapshot;
            var triggerPoint = session.GetTriggerPoint(snapshot);
            if (!triggerPoint.HasValue) return;

            var line = triggerPoint.Value.GetContainingLine();
            var lineText = line.GetText();
            int caretPos = triggerPoint.Value.Position - line.Start.Position;

            // Find word boundary under caret
            int startPos = caretPos;
            while (startPos > 0)
            {
                char ch = lineText[startPos - 1];
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == '%' || ch == '@' || ch == '?')
                {
                    startPos--;
                }
                else
                {
                    break;
                }
            }

            var trackingSpan = snapshot.CreateTrackingSpan(
                new SnapshotSpan(line.Start + startPos, triggerPoint.Value),
                SpanTrackingMode.EdgeInclusive);

            // 1. Initialize static completions
            if (_cachedStaticCompletions == null)
            {
                _cachedStaticCompletions = AsmCompletionData.Items
                    .Select(item => new Completion(
                        item.Text,
                        item.Text,
                        $"[{item.Category}] {item.Text}\n\n{item.Description}",
                        null,
                        item.Category))
                    .ToList();
            }

            var allCompletions = new List<Completion>(_cachedStaticCompletions);

            // 2. Dynamic scanning: Extract all user labels and procedures in current file
            try
            {
                var fullDoc = snapshot.GetText();
                var localSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Matches: my_proc proc, my_label:, my_var dw/db/dd
                var labelMatches = Regex.Matches(fullDoc, @"(?m)^\s*([a-zA-Z_?@][a-zA-Z0-9_?@]*)\s*(?::|proc|equ|=|\b(?:db|dw|dd|dq|dt|byte|word|dword|qword)\b)", RegexOptions.IgnoreCase);
                foreach (Match m in labelMatches)
                {
                    if (m.Groups.Count > 1)
                    {
                        var sym = m.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(sym) && sym.Length > 1)
                        {
                            localSymbols.Add(sym);
                        }
                    }
                }

                foreach (var sym in localSymbols)
                {
                    if (!allCompletions.Any(c => string.Equals(c.InsertionText, sym, StringComparison.OrdinalIgnoreCase)))
                    {
                        allCompletions.Add(new Completion(
                            sym,
                            sym,
                            $"[Symbol] 当前文档定义的局部过程/变量标号: {sym}",
                            null,
                            "Symbol"));
                    }
                }
            }
            catch
            {
                // Fallback gracefully on parsing error
            }

            completionSets.Add(new CompletionSet(
                "AsmTokens",
                "Assembly",
                trackingSpan,
                allCompletions,
                Enumerable.Empty<Completion>()));
        }

        public void Dispose()
        {
            _isDisposed = true;
        }
    }
}
