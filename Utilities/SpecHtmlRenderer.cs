// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System.Text;

namespace BIBIM_MVP
{
    /// <summary>
    /// Renders a CodeSpecification into an HTML fragment for display in the chat WebBrowser.
    /// Extracted from SpecGenerator to separate HTML rendering concerns from spec generation logic.
    ///
    /// CSS classes used: spec-card, spec-header, spec-section, spec-label,
    ///                   spec-questions, spec-actions, spec-btn confirm, spec-btn modify
    /// </summary>
    internal static class SpecHtmlRenderer
    {
        /// <summary>
        /// Formats a <see cref="CodeSpecification"/> as an HTML card fragment.
        /// Includes Inputs, ProcessingSteps, Output, optional ClarifyingQuestions,
        /// and Confirm / Request-Changes action buttons.
        /// </summary>
        public static string FormatSpecificationHtml(CodeSpecification spec)
        {
            if (spec == null)
                return LocalizationService.Get("Spec_Format_NoSpec");

            var html = new StringBuilder();
            html.Append("<div class='spec-card'>");
            html.Append($"<div class='spec-header'>{Escape(LocalizationService.Format("Spec_Format_Header", spec.RevisionNumber))}</div>");

            // Inputs section
            html.Append($"<div class='spec-section'><div class='spec-label'>{Escape(LocalizationService.Get("Spec_Format_InputLabel"))}</div><ul>");
            foreach (var input in spec.Inputs)
            {
                html.Append($"<li><strong>{Escape(input.Name)}</strong> ({Escape(input.Type)}): {Escape(input.Description)}</li>");
            }
            html.Append("</ul></div>");

            // Processing steps section
            html.Append($"<div class='spec-section'><div class='spec-label'>{Escape(LocalizationService.Get("Spec_Format_ProcessLabel"))}</div><ol>");
            foreach (var step in spec.ProcessingSteps)
            {
                html.Append($"<li>{Escape(step)}</li>");
            }
            html.Append("</ol></div>");

            // Output section
            html.Append($"<div class='spec-section'><div class='spec-label'>{Escape(LocalizationService.Get("Spec_Format_OutputLabel"))}</div>");
            html.Append($"<p>{Escape(spec.Output.Type)}: {Escape(spec.Output.Description)}");
            if (!string.IsNullOrEmpty(spec.Output.Unit))
            {
                html.Append($" ({Escape(spec.Output.Unit)})");
            }
            html.Append("</p></div>");

            // Questions section (if any) — uses spec-questions CSS class for orange accent styling
            if (spec.ClarifyingQuestions != null && spec.ClarifyingQuestions.Count > 0)
            {
                html.Append($"<div class='spec-section spec-questions'><div class='spec-label'>{Escape(LocalizationService.Get("Spec_Format_QuestionsLabel"))}</div><ul>");
                foreach (var question in spec.ClarifyingQuestions)
                {
                    html.Append($"<li>{Escape(question)}</li>");
                }
                html.Append("</ul></div>");
            }

            // Action buttons
            html.Append("<div class='spec-actions'>");
            html.Append($"<button class='spec-btn confirm' onclick='window.external.ConfirmSpec()'>{Escape(LocalizationService.Get("Spec_Format_ButtonConfirm"))}</button>");
            html.Append($"<button class='spec-btn modify' onclick='window.external.RequestChanges()'>{Escape(LocalizationService.Get("Spec_Format_ButtonModify"))}</button>");
            html.Append("</div>");

            html.Append("</div>");
            return html.ToString();
        }

        // Escapes HTML special characters. Does NOT convert newlines — spec text is structured data,
        // not free-form user chat, so newline handling is left to the caller.
        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }
    }
}
