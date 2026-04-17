// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using Markdig;

namespace BIBIM_MVP
{
    /// <summary>
    /// Markdown to HTML renderer for AI responses
    /// Requirements: 5.2, 5.3, 5.4, 5.5, 5.6, 5.7
    /// </summary>
    public static class MarkdownRenderer
    {
        /// <summary>
        /// CSS styles for rendered HTML
        /// Requirement 5.3: Code blocks with gray background and monospace font
        /// Requirement 5.4: Bold text with clear visual emphasis
        /// Requirement 5.5: Inline code with distinct background
        /// Requirement 5.6: Lists with proper indentation
        /// Requirement 5.7: Headers with appropriate font sizes
        /// </summary>
        private static readonly string CssStyles = @"
            body {
                font-family: 'Pretendard', 'Malgun Gothic', 'Segoe UI', sans-serif;
                font-size: 14px;
                line-height: 1.6;
                color: #E1E1E1;
                background-color: #181818;
                margin: 0;
                padding: 12px;
                user-select: text;
                -webkit-user-select: text;
                -moz-user-select: text;
                -ms-user-select: text;
            }
            
            /* Headers */
            h1 { font-size: 1.8em; font-weight: 600; margin: 16px 0 8px 0; color: #FFFFFF; }
            h2 { font-size: 1.5em; font-weight: 600; margin: 14px 0 6px 0; color: #FFFFFF; }
            h3 { font-size: 1.25em; font-weight: 600; margin: 12px 0 4px 0; color: #FFFFFF; }
            h4 { font-size: 1.1em; font-weight: 600; margin: 10px 0 4px 0; color: #FFFFFF; }
            
            /* Paragraphs */
            p { margin: 8px 0; }
            
            /* Bold text */
            strong, b { font-weight: 600; color: #FFFFFF; }
            
            /* Italic text */
            em, i { font-style: italic; }
            
            /* Code blocks */
            pre {
                background-color: #2D2D30;
                border: 1px solid #3E3E42;
                border-radius: 6px;
                padding: 12px;
                margin: 12px 0;
                overflow-x: auto;
            }
            
            pre code {
                font-family: 'Consolas', 'D2Coding', 'Courier New', monospace;
                font-size: 13px;
                color: #9CDCFE;
                background: none;
                padding: 0;
                border: none;
            }
            
            /* Inline code */
            code {
                font-family: 'Consolas', 'D2Coding', 'Courier New', monospace;
                font-size: 13px;
                background-color: #3E3E42;
                color: #CE9178;
                padding: 2px 6px;
                border-radius: 4px;
            }
            
            /* Lists */
            ul, ol {
                margin: 8px 0;
                padding-left: 24px;
            }
            
            li {
                margin: 4px 0;
            }
            
            /* Blockquotes */
            blockquote {
                border-left: 3px solid #007ACC;
                margin: 12px 0;
                padding: 8px 16px;
                background-color: #252526;
                color: #CCCCCC;
            }
            
            /* Links */
            a {
                color: #007ACC;
                text-decoration: none;
            }
            
            a:hover {
                text-decoration: underline;
            }
            
            /* Tables */
            table {
                border-collapse: collapse;
                margin: 12px 0;
                width: 100%;
            }
            
            th, td {
                border: 1px solid #3E3E42;
                padding: 8px 12px;
                text-align: left;
            }
            
            th {
                background-color: #2D2D30;
                font-weight: 600;
            }
            
            /* Horizontal rule */
            hr {
                border: none;
                border-top: 1px solid #3E3E42;
                margin: 16px 0;
            }
        ";

        private static readonly MarkdownPipeline Pipeline;
        private static readonly MarkdownPipeline GuideTextPipeline;

        static MarkdownRenderer()
        {
            // Configure Markdig pipeline with specific extensions
            Pipeline = new MarkdownPipelineBuilder()
                .UseEmphasisExtras()        // ~~strikethrough~~, etc.
                .UseAutoLinks()             // Auto-detect URLs
                .UseTaskLists()             // - [ ] checkboxes
                .UsePipeTables()            // | table | syntax |
                .UseAutoIdentifiers()       // Header anchors
                .UseGenericAttributes()     // {.class #id}
                .Build();
            
            // Guide text pipeline: same as above but with IndentedCodeBlockParser disabled
            // This prevents 4-space indented text from being treated as code blocks
            var guideBuilder = new MarkdownPipelineBuilder()
                .UseEmphasisExtras()
                .UseAutoLinks()
                .UseTaskLists()
                .UsePipeTables()
                .UseAutoIdentifiers()
                .UseGenericAttributes();
            
            // Remove IndentedCodeBlockParser from block parsers
            guideBuilder.BlockParsers.TryRemove<Markdig.Parsers.IndentedCodeBlockParser>();
            
            GuideTextPipeline = guideBuilder.Build();
        }

        /// <summary>
        /// Convert markdown string to HTML
        /// Requirement 5.2: Convert markdown to styled HTML
        /// </summary>
        public static string ConvertToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return string.Empty;

            try
            {
                // Preprocess: ensure blank lines around markdown tables for proper Markdig parsing
                string preprocessed = EnsureBlankLinesAroundTables(markdown);
                return Markdown.ToHtml(preprocessed, Pipeline);
            }
            catch (Exception)
            {
                // Fallback: return escaped text if parsing fails
                return System.Net.WebUtility.HtmlEncode(markdown)
                    .Replace("
", "<br/>");
            }
        }

        /// <summary>
        /// Convert markdown to HTML for guide text, removing unwanted code blocks.
        /// Guide text should never render as code blocks even if indented.
        /// </summary>
        public static string ConvertGuideToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return string.Empty;

            try
            {
                // Remove leading whitespace from each line to prevent any code block detection
                var lines = markdown.Split('
');
                var normalizedLines = new System.Collections.Generic.List<string>();
                foreach (var line in lines)
                {
                    normalizedLines.Add(line.TrimStart());
                }
                string normalized = string.Join("
", normalizedLines);
                
                // Ensure blank lines around tables
                string preprocessed = EnsureBlankLinesAroundTables(normalized);
                
                // Use GuideTextPipeline which has IndentedCodeBlockParser removed
                return Markdown.ToHtml(preprocessed, GuideTextPipeline);
            }
            catch (Exception)
            {
                // Fallback: return escaped text if parsing fails
                return System.Net.WebUtility.HtmlEncode(markdown)
                    .Replace("
", "<br/>");
            }
        }

        /// <summary>
        /// Ensures blank lines exist before and after markdown table blocks.
        /// Markdig requires blank lines around tables to parse them correctly.
        /// Without this, tables render as raw pipe characters.
        /// </summary>
        private static string EnsureBlankLinesAroundTables(string markdown)
        {
            var lines = markdown.Split('
');
            var result = new System.Text.StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                bool isTableLine = trimmed.StartsWith("|");
                bool prevIsTableLine = i > 0 && lines[i - 1].TrimStart().StartsWith("|");

                // Add blank line before table start (if previous line is not blank and not a table line)
                if (isTableLine && !prevIsTableLine && i > 0)
                {
                    string prevTrimmed = lines[i - 1].Trim();
                    if (!string.IsNullOrEmpty(prevTrimmed))
                    {
                        result.AppendLine();
                    }
                }

                result.AppendLine(lines[i]);

                // Add blank line after table end (if next line is not a table line)
                bool nextIsTableLine = i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith("|");
                if (isTableLine && !nextIsTableLine && i + 1 < lines.Length)
                {
                    string nextTrimmed = lines[i + 1].Trim();
                    if (!string.IsNullOrEmpty(nextTrimmed))
                    {
                        result.AppendLine();
                    }
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Wrap HTML content in a complete HTML document with styles
        /// </summary>
        public static string WrapInHtmlDocument(string htmlContent)
        {
            return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
    <style>
        {CssStyles}
    </style>
    <script type=""text/javascript"">
        // Enable text selection and copy
        document.onselectstart = function() {{ return true; }};
        document.oncopy = function() {{ return true; }};
        document.oncontextmenu = function() {{ return true; }}; // Enable right-click menu
        
        // Handle Ctrl+C programmatically
        document.onkeydown = function(e) {{
            e = e || window.event;
            if (e.ctrlKey && (e.keyCode === 67 || e.which === 67)) {{ // Ctrl+C
                try {{
                    // IE11 specific clipboard API
                    if (window.clipboardData) {{
                        var sel = document.selection;
                        if (sel) {{
                            var range = sel.createRange();
                            if (range && range.text) {{
                                window.clipboardData.setData('Text', range.text);
                                return false; // Prevent default
                            }}
                        }}
                    }}
                }} catch(ex) {{
                    // Silent fail - let C# handler take over
                }}
            }}
            return true;
        }};
    </script>
</head>
<body>
    {htmlContent}
</body>
</html>";
        }

        /// <summary>
        /// Convert markdown to a complete HTML document ready for WebBrowser display
        /// </summary>
        public static string RenderToHtmlDocument(string markdown)
        {
            string html = ConvertToHtml(markdown);
            return WrapInHtmlDocument(html);
        }

        /// <summary>
        /// Render user message with simple styling (no markdown parsing)
        /// </summary>
        public static string RenderUserMessage(string message)
        {
            string escaped = System.Net.WebUtility.HtmlEncode(message ?? string.Empty)
                .Replace("
", "<br/>");
            
            return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <style>
        body {{
            font-family: 'Pretendard', 'Malgun Gothic', 'Segoe UI', sans-serif;
            font-size: 14px;
            line-height: 1.6;
            color: #FFFFFF;
            background-color: #0070F3;
            margin: 0;
            padding: 12px;
            user-select: text;
            -webkit-user-select: text;
            -moz-user-select: text;
            -ms-user-select: text;
        }}
    </style>
    <script type=""text/javascript"">
        document.onselectstart = function() {{ return true; }};
        document.oncopy = function() {{ return true; }};
        document.oncontextmenu = function() {{ return true; }};
        document.onkeydown = function(e) {{
            e = e || window.event;
            if (e.ctrlKey && (e.keyCode === 67 || e.which === 67)) {{
                try {{
                    if (window.clipboardData) {{
                        var sel = document.selection;
                        if (sel) {{
                            var range = sel.createRange();
                            if (range && range.text) {{
                                window.clipboardData.setData('Text', range.text);
                                return false;
                            }}
                        }}
                    }}
                }} catch(ex) {{ }}
            }}
            return true;
        }};
    </script>
</head>
<body>
    {escaped}
</body>
</html>";
        }
    }
}
