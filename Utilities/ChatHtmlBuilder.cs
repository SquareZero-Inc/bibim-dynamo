// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System.Web;

namespace BIBIM_MVP
{
    /// <summary>
    /// Builds chat bubble HTML fragments used throughout ChatWorkspaceViewModel.
    /// Centralizes all bubble markup so style changes only require edits here.
    ///
    /// Color legend:
    ///   Error / ValidationBlock : #EF4444 (red)
    ///   Question / Info          : #007ACC (blue)
    ///   Success / Guide          : #4ADE80 (green)
    ///   Warning / Modify         : #F59E0B (amber)
    ///   Cancel / Muted           : #6B7280 (gray)
    /// </summary>
    internal static class ChatHtmlBuilder
    {
        // ── Escape helpers ────────────────────────────────────────────────

        /// <summary>Escapes text for safe HTML embedding (including newlines → br).</summary>
        public static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("\n", "<br/>");
        }

        /// <summary>Escapes text for &lt;pre&gt;&lt;code&gt; blocks (preserves newlines).</summary>
        public static string EscapeCode(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        // ── Basic bubble types ────────────────────────────────────────────

        /// <summary>Plain user message bubble.</summary>
        public static string UserBubble(string escapedText)
        {
            return $"<div class='message user'><div class='bubble'>{escapedText}</div></div>";
        }

        /// <summary>Plain AI message bubble with pre-rendered inner HTML.</summary>
        public static string AiBubble(string innerHtml)
        {
            return $"<div class='message ai'><div class='bubble'>{innerHtml}</div></div>";
        }

        // ── Coloured accent bubbles ───────────────────────────────────────

        /// <summary>Red-bordered error bubble. Message is a single error string.</summary>
        public static string ErrorBubble(string escapedMessage)
        {
            return $@"<div class='message ai'>
    <div class='bubble' style='border-left: 4px solid #EF4444;'>
        <p class='error'>{escapedMessage}</p>
    </div>
</div>";
        }

        /// <summary>
        /// Interactive question form bubble with option chips and free-text fallback per question.
        /// Calls window.external.SubmitQuestionAnswers(json) on submit.
        /// <paramref name="escapedTitle"/> is the bold header.
        /// <paramref name="escapedIntro"/> is the intro sentence.
        /// <paramref name="questions"/> is the list of structured questions with options.
        /// <paramref name="otherPlaceholder"/> is placeholder text for the free-text field.
        /// <paramref name="submitLabel"/> is the submit button label.
        /// <paramref name="validationMsg"/> is shown when a question is unanswered.
        /// </summary>
        public static string QuestionFormBubble(
            string escapedTitle,
            string escapedIntro,
            System.Collections.Generic.List<ClarifyingQuestion> questions,
            string otherPlaceholder,
            string submitLabel,
            string validationMsg)
        {
            if (questions == null || questions.Count == 0)
                return QuestionBubble(escapedTitle, escapedIntro);

            // Unique form ID to avoid collisions when multiple question bubbles appear
            string fid = "bqf" + System.Math.Abs(System.Guid.NewGuid().GetHashCode()).ToString();

            var sb = new System.Text.StringBuilder();
            sb.Append("<div class='message ai'><div class='bubble' style='border-left:4px solid #007ACC;'>");
            sb.AppendFormat("<p><strong style='color:#007ACC;'>{0}</strong></p>", escapedTitle);
            sb.AppendFormat("<p>{0}</p>", escapedIntro);
            sb.AppendFormat("<div id='{0}'>", fid);

            for (int i = 0; i < questions.Count; i++)
            {
                var q = questions[i];
                string escapedQ = Escape(q.Question);
                sb.AppendFormat(
                    "<div id='{0}_q{1}' style='margin:10px 0;padding:8px;background:#1A2030;border-radius:6px;'>",
                    fid, i);
                sb.AppendFormat("<p style='font-weight:bold;margin:0 0 6px 0;'>{0}. {1}</p>", i + 1, escapedQ);

                // Option chips
                sb.Append("<div style='display:flex;flex-wrap:wrap;gap:6px;margin-bottom:6px;'>");
                if (q.Options != null)
                {
                    foreach (var opt in q.Options)
                    {
                        string escapedOpt = Escape(opt);
                        sb.AppendFormat(
                            "<span class='q-chip' " +
                            "onclick=\"bibimChip(this,'{0}',{1})\" " +
                            "data-val=\"{2}\" " +
                            "style='display:inline-block;padding:4px 10px;border:1px solid #007ACC;" +
                            "border-radius:12px;cursor:pointer;font-size:0.88em;" +
                            "background:#1A1A2E;color:#4FC3F7;user-select:none;'>" +
                            "{2}</span>",
                            fid, i, escapedOpt);
                    }
                }
                sb.Append("</div>");

                // Free-text "other" input
                sb.AppendFormat(
                    "<input type='text' id='{0}_oth{1}' " +
                    "placeholder='{2}' " +
                    "oninput=\"bibimOther(this,'{0}',{1})\" " +
                    "style='width:100%;box-sizing:border-box;padding:4px 8px;" +
                    "border:1px solid #404060;border-radius:4px;font-size:0.88em;" +
                    "background:#141420;color:#E0E0E0;'/>",
                    fid, i, otherPlaceholder);

                sb.Append("</div>"); // close q-card
            }

            // Submit + validation message
            sb.AppendFormat(
                "<div style='margin-top:10px;'>" +
                "<button id='{0}_sub' onclick=\"bibimSubmit('{0}',{1})\" " +
                "style='padding:6px 18px;background:#007ACC;color:white;border:none;" +
                "border-radius:4px;cursor:pointer;font-size:0.9em;'>{2}</button>" +
                "<span id='{0}_err' style='display:none;color:#EF4444;font-size:0.85em;margin-left:8px;'>{3}</span>" +
                "</div>",
                fid, questions.Count, submitLabel, validationMsg);

            sb.Append("</div>"); // close form div

            // Inline JS — functions defined once globally, state stored per form ID
            // Uses var/function syntax for IE-engine compatibility (WebBrowser control)
            sb.AppendFormat(@"
<script type='text/javascript'>
(function(fid, qcount) {{
  if (!window._bqf) window._bqf = {{}};
  var _done = document.getElementById('_bqf_done_' + fid);
  window._bqf[fid] = {{ count: qcount, answers: {{}}, submitted: _done !== null }};
  if (_done !== null) {{ var _dsub = document.getElementById(fid + '_sub'); if (_dsub) {{ _dsub.disabled = true; _dsub.style.opacity = '0.5'; }} }}

  if (!window.bibimChip) {{
    window.bibimChip = function(el, fid, qi) {{
      var s = window._bqf[fid];
      if (!s || s.submitted) return;
      var card = document.getElementById(fid + '_q' + qi);
      if (!card) return;
      var chips = card.getElementsByClassName('q-chip');
      for (var i = 0; i < chips.length; i++) {{
        chips[i].style.background = '#1A1A2E';
        chips[i].style.color = '#4FC3F7';
        chips[i].style.fontWeight = '';
      }}
      el.style.background = '#007ACC';
      el.style.color = 'white';
      el.style.fontWeight = 'bold';
      var oth = document.getElementById(fid + '_oth' + qi);
      if (oth) oth.value = '';
      s.answers[qi] = el.getAttribute('data-val');
    }};
    window.bibimOther = function(el, fid, qi) {{
      var s = window._bqf[fid];
      if (!s || s.submitted) return;
      var card = document.getElementById(fid + '_q' + qi);
      if (!card) return;
      var chips = card.getElementsByClassName('q-chip');
      for (var i = 0; i < chips.length; i++) {{
        chips[i].style.background = '#1A1A2E';
        chips[i].style.color = '#4FC3F7';
        chips[i].style.fontWeight = '';
      }}
      s.answers[qi] = el.value;
    }};
    window.bibimSubmit = function(fid, count) {{
      var s = window._bqf[fid];
      if (!s || s.submitted) return;
      for (var i = 0; i < count; i++) {{
        var oth = document.getElementById(fid + '_oth' + i);
        if (oth && oth.value.trim()) s.answers[i] = oth.value.trim();
      }}
      for (var i = 0; i < count; i++) {{
        if (!s.answers[i] || !s.answers[i].trim()) {{
          var err = document.getElementById(fid + '_err');
          if (err) err.style.display = 'inline';
          return;
        }}
      }}
      s.submitted = true;
      var sub = document.getElementById(fid + '_sub');
      if (sub) {{ sub.disabled = true; sub.style.opacity = '0.5'; }}
      var payload = [];
      var cards = document.getElementById(fid).getElementsByClassName('q-chip');
      for (var i = 0; i < count; i++) {{
        var card = document.getElementById(fid + '_q' + i);
        var qtxt = card ? card.getElementsByTagName('p')[0].innerText.replace(/^\d+\.\s*/, '') : '';
        payload.push({{ question: qtxt, answer: s.answers[i] }});
      }}
      window.external.SubmitQuestionAnswers(fid, JSON.stringify(payload));
    }};
  }}
}})('{0}', {1});
</script>", fid, questions.Count);

            sb.Append("</div></div>"); // close bubble + message
            return sb.ToString();
        }

        /// <summary>
        /// Blue-bordered question/info bubble.
        /// <paramref name="escapedTitle"/> is bolded; <paramref name="bodyHtml"/> is rendered as-is.
        /// </summary>
        public static string QuestionBubble(string escapedTitle, string bodyHtml)
        {
            return $@"<div class='message ai'>
    <div class='bubble' style='border-left: 4px solid #007ACC;'>
        <p><strong style='color: #007ACC;'>{escapedTitle}</strong></p>
        <p>{bodyHtml}</p>
    </div>
</div>";
        }

        /// <summary>
        /// Red-bordered validation block bubble.
        /// <paramref name="escapedTitle"/> is bolded; <paramref name="bodyHtml"/> is rendered as-is.
        /// </summary>
        public static string ValidationBlockBubble(string escapedTitle, string bodyHtml)
        {
            return $@"<div class='message ai'>
    <div class='bubble' style='border-left: 4px solid #EF4444;'>
        <p><strong style='color: #EF4444;'>{escapedTitle}</strong></p>
        {bodyHtml}
    </div>
</div>";
        }

        /// <summary>
        /// Green-bordered guide bubble.
        /// <paramref name="escapedTitle"/> is bolded; <paramref name="guideHtml"/> is rendered as-is.
        /// </summary>
        public static string GuideBubble(string escapedTitle, string guideHtml)
        {
            return $@"<div class='message ai'>
    <div class='bubble' style='border-left: 4px solid #4ADE80;'>
        <p><strong style='color: #4ADE80;'>{escapedTitle}</strong></p>
        {guideHtml}
    </div>
</div>";
        }

        /// <summary>Amber-bordered modify/warning bubble with title and body text.</summary>
        public static string ModifyBubble(string escapedTitle, string escapedBody, string escapedHint = null)
        {
            string hint = string.IsNullOrEmpty(escapedHint)
                ? string.Empty
                : $"<p><em>{escapedHint}</em></p>";
            return $@"<div class='message ai'>
    <div class='bubble' style='border-left: 4px solid #F59E0B;'>
        <p><strong style='color: #F59E0B;'>{escapedTitle}</strong></p>
        <p>{escapedBody}</p>
        {hint}
    </div>
</div>";
        }

        /// <summary>Gray-bordered cancellation bubble.</summary>
        public static string CancelBubble(string escapedMessage)
        {
            return $@"<div class='message ai'>
    <div class='bubble' style='border-left: 4px solid #6B7280;'>
        <p style='color: #9CA3AF;'>{escapedMessage}</p>
    </div>
</div>";
        }

        // ── Code output bubbles ───────────────────────────────────────────

        /// <summary>Success paragraph inside an AI bubble (for code injection confirmation).</summary>
        public static string CodeSuccessBubble(string escapedMessage)
        {
            return $"<div class='message ai'><div class='bubble'><p class='success'>{escapedMessage}</p></div></div>";
        }

        /// <summary>
        /// Collapsible code block bubble with external copy button.
        /// <paramref name="headerLabel"/> is the toggle header text (e.g. "View generated code (42 lines)").
        /// <paramref name="copyLabel"/> is the copy button label.
        /// </summary>
        public static string CodeToggleBubble(string escapedCode, string headerLabel, string copyLabel)
        {
            return $@"<div class='message ai'>
    <div class='bubble'>
        <div class='code-toggle-header' onclick='toggleCode(this)'>
            <span class='toggle-icon'>▶</span> {headerLabel}
        </div>
        <div class='code-content' style='display:none;'>
            <div class='code-block'>
                <pre><code>{escapedCode}</code></pre>
            </div>
        </div>
        <button class='code-copy-btn' onclick='copyCodeFromBlock(this)' style='margin-top:8px;'>📋 {copyLabel}</button>
    </div>
</div>";
        }

        // ── Simple inline-error shortcuts ─────────────────────────────────

        /// <summary>Minimal one-liner error paragraph inside a plain AI bubble.</summary>
        public static string SimpleErrorBubble(string escapedMessage)
        {
            return $"<div class='message ai'><div class='bubble'><p class='error'>{escapedMessage}</p></div></div>";
        }

        /// <summary>Minimal one-liner warning paragraph inside a plain AI bubble.</summary>
        public static string SimpleWarningBubble(string escapedMessage)
        {
            return $"<div class='message ai'><div class='bubble'><p class='warning'>{escapedMessage}</p></div></div>";
        }
    }
}
