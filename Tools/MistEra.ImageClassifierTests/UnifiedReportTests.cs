using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

static class UnifiedReportTests
{
    public static void Run(ImageRules rules, Assertions checks)
    {
        // Synthetic results only: BuildAnalysis must not require images, a tag store or a real folder.
        var category = new CategoryRule { id = "report_actor" };
        var otherCategory = new CategoryRule { id = "report_other", rename = false };
        var knownName = rules.nameSets.Values.SelectMany(set => set).First(n => !string.IsNullOrEmpty(n.name));
        var analysis = new ImageAnalysisResult { folder = "synthetic report folder", rules = rules, folderDecides = true };
        ImageClassification Row(string path, ClassificationStatus status, CategoryRule cat, bool named = false)
        {
            var row = new ImageClassification
            {
                record = new ImageTagRecord { path = path }, status = status, category = cat,
                name = named ? knownName.name : null, nameRule = named ? knownName : null, score = 0.63f
            };
            analysis.results.Add(row);
            return row;
        }

        const string unsafePath = "report actor/space # \"quote\" & under_score '<unsafe>'.png";
        const string safeUrl = "./report%20actor/space%20%23%20%22quote%22%20%26%20under_score%20%27%3Cunsafe%3E%27.png";
        const string safeText = "report actor/space # &quot;quote&quot; &amp; under_score &#39;&lt;unsafe&gt;&#39;.png";
        var confident = Row(unsafePath, ClassificationStatus.Confident, category, true);
        confident.record.originalName = "first_<source> & \"quoted\".png";
        confident.filenameHint = knownName.name;
        var low = Row("report_actor/low_score.png", ClassificationStatus.LowConfidence, category, true);
        low.reasons.Add("synthetic low confidence reason");
        var unmatched = Row("no actor/unmatched_image.png", ClassificationStatus.Unmatched, null);
        unmatched.reasons.Add("synthetic unmatched reason");
        var kept = Row("outside/kept_out.png", ClassificationStatus.Confident, category, true);
        kept.keptOut = true;
        kept.record.manual = "out";
        var folder = Row("report_other/original_image.png", ClassificationStatus.Confident, otherCategory);
        folder.byFolder = true;
        folder.rulesCategoryId = category.id;
        var unnamed = Row("report_actor/no_name.png", ClassificationStatus.Unmatched, category);
        unnamed.reasons.Add("synthetic no name reason");

        foreach (var row in new[] { low, unmatched, kept, unnamed })
        {
            row.record.originalName = "original_" + row.record.path.Split('/').Last();
            row.record.rating.Add("general", 0.91f);
            row.record.character.Add("reportcharacter", 0.73f);
            row.record.general.Add("report_tag", 0.87f);
            row.record.general.Add("belowthreshold", 0.01f);
            row.record.face = true;
            row.record.vlm = "reportemotion";
            row.record.faceTags = new System.Collections.Generic.Dictionary<string, float> { { "reportface", 0.62f } };
        }
        // Full per-image tags must not be truncated to the frequency table's 60 entries.
        for (int i = 0; i < 65; i++) low.record.general.Add("lowtag" + i.ToString("00", CultureInfo.InvariantCulture), 0.02f);

        var report = ImageAnalysis.BuildAnalysis(analysis);
        string Section(string text, string start, string end)
        {
            var begin = text.IndexOf(start, StringComparison.Ordinal);
            checks.Check(begin >= 0, "Missing section: " + start);
            var finish = text.IndexOf(end, begin + start.Length, StringComparison.Ordinal);
            checks.Check(finish >= 0, "Missing section boundary: " + end);
            return text.Substring(begin, finish - begin);
        }
        string Md(string text) => Regex.Replace(text.Replace("&", "&amp;"), @"([\\`*_\[\]<>|#])", @"\$1");
        string Url(string path) => "./" + string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
        void Has(string text, string expected) => checks.Check(text.Contains(expected, StringComparison.Ordinal), "Missing report text: " + expected);
        var clusters = Section(report, "## Results by category", "## Rules that matched no image");
        var diagnostics = report.Substring(report.IndexOf("<a id=\"diagnostics\"></a>", StringComparison.Ordinal));
        var rows = Regex.Matches(clusters, @"<tr>\s*<td>[\s\S]*?</tr>").Cast<Match>().Select(m => m.Value).ToArray();
        string ClusterRow(ImageClassification row) => rows.Single(text => text.Contains("src=\"" + Url(row.record.path) + "\"", StringComparison.Ordinal));

        checks.Test("unified report previews every result in category clusters at 112px", () =>
        {
            checks.Equal(analysis.results.Count, rows.Length, "One cluster row per record");
            checks.Equal(analysis.results.Count, Regex.Matches(clusters, "<img ").Count, "One cluster preview per record");
            foreach (var row in analysis.results)
            {
                var html = ClusterRow(row);
                Has(html, "<a href=\"" + Url(row.record.path) + "\"><img src=\"" + Url(row.record.path) + "\"");
                Has(html, "width=\"112\" height=\"112\"");
                Has(html, "style=\"object-fit:contain\"");
                Has(html, "loading=\"lazy\"");
                checks.Equal(2, Regex.Matches(html, "href=\"" + Regex.Escape(Url(row.record.path)) + "\"").Count,
                    "Thumbnail and path both link to original: " + row.record.path);
            }
            Has(report, "| Analyzed (all previewed below) | 6 |");
            Has(report, "| Confident | 2 |");
            Has(report, "| Low confidence | 1 |");
            Has(report, "| Unmatched | 2 |");
            Has(report, "| Moved out of a category folder by you (never moved again) | 1 |");
        });

        checks.Test("unified report encodes small preview links and escapes unsafe HTML", () =>
        {
            var html = ClusterRow(confident);
            Has(html, "href=\"" + safeUrl + "\"");
            Has(html, "src=\"" + safeUrl + "\"");
            Has(html, "alt=\"" + safeText + "\"");
            Has(html, ">" + safeText + "</a>");
            Has(html, "Original name: first_&lt;source&gt; &amp; &quot;quoted&quot;.png");
            checks.Check(!html.Contains(unsafePath, StringComparison.Ordinal), "Raw path leaked into HTML");
            checks.Check(!html.Contains("<unsafe>", StringComparison.Ordinal) && !html.Contains("<source>", StringComparison.Ordinal),
                "Path or provenance became an HTML element");
            checks.Check(!Regex.IsMatch(clusters, @"(?:href|src)=""(?!#)[^""]*[ #&<>']"), "Unsafe character in preview URL");
        });

        checks.Test("unified report groups by category then name including unnamed results", () =>
        {
            var actor = Section(clusters, "### report\\_actor (4)", "\n### ");
            Has(actor, "#### Name / emotion: " + Md(knownName.name) + " (3)");
            Has(actor, "Confident: 2; low confidence: 1; unmatched: 0; kept out: 1.");
            Has(actor, "#### Name / emotion: (no name matched) (1)");
            Has(actor, "Confident: 0; low confidence: 0; unmatched: 1; kept out: 0.");
            Has(clusters, "### report\\_other (1)");
            Has(clusters, "#### Name / emotion: (original file names kept) (1)");
            Has(clusters, "### (no category) (1)");
            checks.Equal(4, Regex.Matches(clusters, @"(?m)^#### Name / emotion:").Count, "Category/name cluster count");
        });

        checks.Test("unified report labels confidence provenance and filename IDs without fake scores", () =>
        {
            Has(ClusterRow(confident), "<td>Confident<br>Category source: rules/tags; name source: filename ID " + knownName.name);
            Has(ClusterRow(confident), "Score: n/a (filename ID, not a tag score)");
            checks.Check(!ClusterRow(confident).Contains("Rule score:", StringComparison.Ordinal), "Filename ID must not appear as a rule score");
            Has(ClusterRow(low), "<td>Low confidence<br>Category source: rules/tags; name source: rules/tags<br>Rule score: 0.63");
            Has(ClusterRow(unmatched), "<td>Unmatched<br>");
            Has(ClusterRow(kept), "<td>Confident (kept out)<br>");
            Has(ClusterRow(folder), "Category source: folder; name source: none (original filename kept or unmatched)");
            Has(ClusterRow(folder), "Name score: n/a");
            foreach (var row in new[] { low, unmatched, kept, unnamed })
                Has(ClusterRow(row), "Original name: " + row.record.originalName);
            Has(report, "## Your folders vs the rules (1)");
            Has(report, "| [report\\_other/original\\_image.png](<./report_other/original_image.png>) | report\\_other | report\\_actor |");
        });

        checks.Test("unified diagnostics retain all low unmatched and kept-out tag contents", () =>
        {
            Has(diagnostics, "Unmatched: **2**");
            Has(diagnostics, "Low confidence: **1**");
            Has(diagnostics, "Moved out by you: **1**");
            Has(diagnostics, "### Unmatched (never moved) (2)");
            Has(diagnostics, "### Low confidence (1)");
            checks.Equal(4, Regex.Matches(diagnostics, @"(?m)^##### \[").Count, "One diagnostic record per nonconfident/kept-out image");
            foreach (var row in new[] { low, unmatched, kept, unnamed })
            {
                var header = "##### [" + Md(row.record.path) + "](<" + Url(row.record.path) + ">)";
                var start = diagnostics.IndexOf(header, StringComparison.Ordinal);
                checks.Check(start >= 0, "Missing diagnostics for " + row.record.path);
                var end = diagnostics.IndexOf("\n###", start + header.Length, StringComparison.Ordinal);
                var body = end < 0 ? diagnostics.Substring(start) : diagnostics.Substring(start, end - start);
                Has(body, "width=\"112\" height=\"112\"");
                Has(body, "- Original name: " + Md(row.record.originalName));
                Has(body, "- Rating: general 0.91");
                Has(body, "- Character: reportcharacter 0.73");
                Has(body, "VLM **reportemotion**");
                Has(body.Replace("**", ""), "reportface 0.62");
                foreach (var tag in row.record.general)
                    Has(body.Replace("**", ""), Md(tag.Key) + " " + tag.Value.ToString("0.00", CultureInfo.InvariantCulture));
                foreach (var reason in row.reasons) Has(body, Md(reason));
            }
            checks.Check(!diagnostics.Contains(safeUrl, StringComparison.Ordinal), "Ordinary confident record must not enter diagnostics");
        });

        checks.Test("unified report points only to internal diagnostics not the legacy unclassified report", () =>
        {
            Has(report, "[diagnostics](#diagnostics)");
            checks.Equal(1, Regex.Matches(report, "<a id=\"diagnostics\"></a>").Count, "One diagnostics anchor");
            foreach (var row in analysis.results)
            {
                bool expected = row.status != ClassificationStatus.Confident || row.keptOut;
                checks.Equal(expected, ClusterRow(row).Contains("<a href=\"#diagnostics\">Full diagnostics</a>", StringComparison.Ordinal),
                    "Diagnostic link eligibility: " + row.record.path);
            }
            checks.Equal(4, Regex.Matches(clusters, "href=\"#diagnostics\"").Count, "Diagnostic anchor link count");
            checks.Check(!report.Contains("_unclassified.md", StringComparison.OrdinalIgnoreCase), "Legacy report reference must not be emitted");
        });
    }
}
