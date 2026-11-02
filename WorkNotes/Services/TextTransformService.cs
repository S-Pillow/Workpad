using System;

namespace WorkNotes.Services
{
    /// <summary>
    /// Executes a pure text transform selection-first with a whole-document
    /// fallback. One call = one replacement = one undo step. Never mutates on
    /// failure; reports precise errors instead.
    /// </summary>
    public static class TextTransformService
    {
        /// <summary>
        /// Apply a transform: uses selected text when non-empty, else the whole
        /// document. Returns the replacement result plus the range it applies to
        /// (offset/length in the original text) so the caller can do one replace.
        /// </summary>
        public static TransformOutcome Apply(
            string documentText,
            int selectionStart,
            int selectionLength,
            Func<string, TransformResult> transform)
        {
            var hasSelection = selectionLength > 0
                && selectionStart >= 0
                && selectionStart + selectionLength <= documentText.Length;

            var input = hasSelection
                ? documentText.Substring(selectionStart, selectionLength)
                : documentText;
            var offset = hasSelection ? selectionStart : 0;

            var result = transform(input);
            if (!result.Success)
                return TransformOutcome.Failed(result.ErrorMessage ?? "The command could not run on this text.");
            if (result.ChangeCount == 0)
                return TransformOutcome.NoOp("Nothing to change.");

            return new TransformOutcome
            {
                Success = true,
                Offset = offset,
                Length = input.Length,
                Replacement = result.Text,
                ChangeCount = result.ChangeCount,
            };
        }
    }

    public class TransformOutcome
    {
        public bool Success { get; init; }
        public bool NoChange { get; init; }
        public int Offset { get; init; }
        public int Length { get; init; }
        public string Replacement { get; init; } = string.Empty;
        public int ChangeCount { get; init; }
        public string? Message { get; init; }

        public static TransformOutcome Failed(string msg) => new() { Success = false, Message = msg };
        public static TransformOutcome NoOp(string msg) => new() { Success = false, NoChange = true, Message = msg };
    }
}
