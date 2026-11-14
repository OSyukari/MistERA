using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

static class UnifiedReportTests
{
    public static void Run(ImageRules rules, Assertions checks)
    {
        // Synthetic results only: the cache and report JSON must not require images, a tag store or a real folder.
        var category = new CategoryRule { id = "report_actor" };
        var otherCategory = new CategoryRule { id = "report_other", rename = false };
        var knownName = rules.nameSets.Values.SelectMany(set => set).First(n => !string.IsNullOrEmpty(n.name));
        var analysis = new ImageAnalysisResult { folder = Path.GetFullPath("synthetic report folder"), rules = rules, folderDecides = true };
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

        const string unsafePath = "report actor/space # \"quote\" & under_score '</script><unsafe>'.png";
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
        unnamed.variantFilenameHint = "cum";

        foreach (var row in new[] { low, unmatched, kept, unnamed })
        {
            row.record.originalName = "original_" + row.record.path.Split('/').Last();
            row.record.rating.Add("general", 0.91f);
            row.record.character.Add("reportcharacter", 0.73f);
            row.record.general.Add("report_tag", 0.87f);
            row.record.general.Add("belowthreshold", 0.01f);
            row.record.face = true;
            row.record.vlm = "reportemotion";
            row.record.faceTags = new Dictionary<string, float> { { "reportface", 0.62f } };
        }
        // Full per-image tags must not be truncated.
        for (int i = 0; i < 65; i++) low.record.general.Add("lowtag" + i.ToString("00", CultureInfo.InvariantCulture), 0.02f);

        var cache = ImageReport.BuildFolderCache(analysis, "rules-hash", "10:20", new DateTime(2026, 11, 13, 9, 30, 0));
        ImageReportItem Item(ImageClassification row) => cache.items.Single(i => i.path == row.record.path);

        checks.Test("folder cache counts every result once and keeps kept-out images apart", () =>
        {
            checks.Equal(6, cache.items.Count, "One item per result");
            checks.Equal(6, cache.analyzedCount, "Analyzed");
            checks.Equal(2, cache.confident, "Confident (kept out excluded)");
            checks.Equal(1, cache.low, "Low confidence");
            checks.Equal(2, cache.unmatched, "Unmatched");
            checks.Equal(1, cache.keptOut, "Kept out");
            checks.Equal(1, cache.byFolder, "By folder");
            checks.Equal("2026-11-13 09:30", cache.analyzed, "Analyzed date");
            checks.Equal("rules-hash", cache.rulesHash, "Rules hash");
            checks.Equal("10:20", cache.tagStamp, "Tag stamp");
        });

        checks.Test("folder cache items carry class name, status, provenance and original names", () =>
        {
            checks.Equal(knownName.name, Item(confident).cls, "Class name of a named image");
            checks.Equal("(no name matched)", Item(unnamed).cls, "Class name of an unnamed image");
            checks.Equal("(original file names kept)", Item(folder).cls, "Class name of a rename:false category");
            checks.Equal(null, Item(unmatched).category, "No category");
            checks.Equal("confident", Item(confident).status, "Confident status");
            checks.Equal("low", Item(low).status, "Low status");
            checks.Equal("unmatched", Item(unmatched).status, "Unmatched status");
            checks.Check(Item(kept).keptOut, "Kept out flag");
            checks.Check(Item(folder).byFolder && Item(folder).disagree, "Folder disagreement flags");
            checks.Equal(category.id, Item(folder).rulesSay, "Rules say");
            checks.Equal(confident.record.originalName, Item(confident).original, "Original name kept verbatim");
            foreach (var row in new[] { low, unmatched, kept, unnamed })
                checks.Equal(row.record.originalName, Item(row).original, "Original name: " + row.record.path);
            checks.Check(Item(confident).score.Contains("filename ID"), "Filename ID is not a tag score");
            checks.Equal("report_actor/", Item(low).dest, "Destination of a categorized image");
            checks.Equal(null, Item(kept).dest, "Kept-out images have no destination");
        });

        checks.Test("name used in analysis = filename gave category, name or variant", () =>
        {
            checks.Check(Item(confident).nameUsed, "Name from filename");
            checks.Check(Item(confident).nameHints.Contains("name " + knownName.name), "Name hint listed");
            checks.Check(Item(unnamed).nameUsed, "Variant from filename");
            checks.Check(Item(unnamed).nameHints.Contains("variant cum"), "Variant hint listed");
            foreach (var row in new[] { low, unmatched, kept, folder })
            {
                checks.Check(!Item(row).nameUsed, "Name not used: " + row.record.path);
                checks.Equal(null, Item(row).nameHints, "No hints: " + row.record.path);
            }
            // a state matched in the file name selects nothing any more (state sets are sorted by hand)
            var state = new ImageClassification { record = new ImageTagRecord { path = "x.png" }, state = "sleeping", stateSource = "filename ID sleep" };
            checks.Check(!ImageAnalysis.NameUsed(state), "State from filename is not a use");
        });

        checks.Test("folder cache keeps full diagnostics for images that are not confidently placed", () =>
        {
            foreach (var row in new[] { low, unmatched, kept, unnamed })
            {
                var item = Item(row);
                checks.Equal(row.record.general.Count, item.tags.Count, "All general tags: " + row.record.path);
                checks.Equal("report_tag", (string)item.tags[0][0], "Best tag first: " + row.record.path);
                checks.Equal("reportcharacter", (string)item.chars[0][0], "Character tags: " + row.record.path);
                checks.Equal("general 0.91", item.rating, "Rating: " + row.record.path);
                checks.Check(item.face.Contains("VLM reportemotion") && item.face.Contains("reportface 0.62"), "Face crop: " + row.record.path);
                foreach (var reason in row.reasons) checks.Check(item.why.Contains(reason), "Reason: " + reason);
            }
            checks.Check(Item(low).guess != null && Item(low).guess.StartsWith("report_actor / "), "Best guess for low confidence");
            checks.Equal(null, Item(confident).guess, "No best guess for confident images");
            checks.Equal(null, Item(confident).breakdown, "No breakdown for confident images");
        });

        var dir = Path.Combine(Path.GetTempPath(), "mistera_report_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var reportDir = Path.Combine(dir, "Mods");
            var folderPath = Path.Combine(reportDir, "Assetpacks", "pack #1", "chara");
            var entries = new List<ImageReportFolder>
            {
                new ImageReportFolder { name = "Assetpacks/pack #1/chara", path = folderPath, cache = cache, hasTagFile = true, stale = "rules changed since analysis" },
                new ImageReportFolder { name = "Assetpacks/new", path = Path.Combine(reportDir, "Assetpacks", "new") },
            };
            var json = ImageReport.BuildReportJson(reportDir, "Mods", entries, rules, "rules.json", new DateTime(2026, 11, 13));

            checks.Test("report JSON cannot close its script tag and parses back", () =>
            {
                checks.Check(!json.Contains("</", StringComparison.Ordinal), "Raw </ in report data");
                var data = JObject.Parse(json.Replace("<\\/", "</"));
                var folders = (JArray)data["folders"];
                checks.Equal(2, folders.Count, "Folders");
                checks.Equal("./Assetpacks/pack%20%231/chara/", (string)folders[0]["base"], "Escaped folder URL base");
                checks.Equal("rules changed since analysis", (string)folders[0]["stale"], "Stale reason");
                checks.Equal(6, ((JArray)folders[0]["items"]).Count, "Items of the analyzed folder");
                checks.Check(folders[1]["items"] == null && folders[1]["analyzed"] == null, "Never-analyzed folder has no items");
                checks.Equal(unsafePath, (string)folders[0]["items"].First(i => ((string)i["path"]).StartsWith("report actor/"))["path"], "Unsafe path round trip");
                checks.Check(data["unused"].Type == JTokenType.Object, "Unused rules listed");
                checks.Check(((JArray)data["usedTags"]).Count > 0, "Rule tags listed for bolding");
            });

            checks.Test("report JSON has no unused-rules section without any analyzed folder", () =>
            {
                var empty = ImageReport.BuildReportJson(reportDir, "Mods", new List<ImageReportFolder> { entries[1] }, rules, "rules.json", DateTime.Now);
                checks.Equal(JTokenType.Null, JObject.Parse(empty)["unused"].Type, "Unused");
            });
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        checks.Test("stale reason compares rules hash and tag file stamp", () =>
        {
            checks.Equal(null, ImageReport.StaleReason(cache, "10:20", "rules-hash"), "Up to date");
            checks.Equal("rules changed since analysis", ImageReport.StaleReason(cache, "10:20", "other"), "Rules changed");
            checks.Equal("images tagged, moved or renamed since analysis", ImageReport.StaleReason(cache, "11:20", "rules-hash"), "Tags changed");
            checks.Check(ImageReport.StaleReason(cache, null, "other").Contains("; "), "Both reasons");
            checks.Equal(null, ImageReport.StaleReason(null, "x", "y"), "No cache, nothing stale");
        });

        WorkFolderTests(checks);
        SplitClassifyTests(knownName, checks);
    }

    /// <summary>Classify in two steps: Move keeps the file name, Rename only touches images already in their category folder.</summary>
    static void SplitClassifyTests(NameRule knownName, Assertions checks)
    {
        var folder = Path.Combine(Path.GetTempPath(), "mistera_split_" + Guid.NewGuid().ToString("N"), "pack", "chara");
        var category = new CategoryRule { id = "human" };
        var analysis = new ImageAnalysisResult { folder = folder, rules = new ImageRules(), folderDecides = true };
        ImageClassification Row(string path, ClassificationStatus status, bool byFolder = false)
        {
            var row = new ImageClassification { record = new ImageTagRecord { path = path }, status = status, category = category,
                name = knownName.name, nameRule = knownName, byFolder = byFolder };
            analysis.results.Add(row);
            return row;
        }
        Row("loose/a.png", ClassificationStatus.Confident);
        Row("human/b.png", ClassificationStatus.Confident, true);
        Row("loose/c.png", ClassificationStatus.LowConfidence);
        Row("human/" + knownName.name + "_pack_chara_7.png", ClassificationStatus.Confident, true);

        checks.Test("classify step Move keeps file names and skips images already in their folder", () =>
        {
            var plan = ImageClassifyMover.BuildPlan(analysis, false, ImageClassifyStep.Move);
            checks.Equal(ImageClassifyStep.Move, plan.step, "Step");
            checks.Equal("loose/a.png -> human/a.png", string.Join(" | ", plan.moves.Select(m => m.from + " -> " + m.to)), "Moves");
            checks.Equal(2, plan.alreadyInPlace, "Already in their category folder");
            checks.Equal(1, plan.lowConfidenceHeldBack, "Low confidence held back on a first pass");
            var withLow = ImageClassifyMover.BuildPlan(analysis, true, ImageClassifyStep.Move);
            checks.Check(withLow.moves.Any(m => m.to == "human/c.png"), "Low confidence moved with its own name");
        });

        checks.Test("classify step Rename names only images in their category folder", () =>
        {
            var plan = ImageClassifyMover.BuildPlan(analysis, true, ImageClassifyStep.Rename);
            // numbering continues after the files on disk; the synthetic folder is empty
            checks.Equal("human/b.png -> human/" + knownName.name + "_pack_chara_1.png",
                string.Join(" | ", plan.moves.Select(m => m.from + " -> " + m.to)), "Renamed in place");
            checks.Equal(2, plan.notInCategoryFolder, "Images outside category folders left alone");
            checks.Equal(1, plan.alreadyInPlace, "Final name kept");
        });

        checks.Test("classify step Both still moves and renames in one plan", () =>
        {
            var plan = ImageClassifyMover.BuildPlan(analysis, false);
            checks.Check(plan.moves.Any(m => m.from == "loose/a.png" && m.to.StartsWith("human/" + knownName.name + "_pack_chara_")), "Moved and renamed");
        });
    }

    static void WorkFolderTests(Assertions checks)
    {
        var mods = Path.Combine(Path.GetTempPath(), "mistera_workfolders_" + Guid.NewGuid().ToString("N"), "Mods");
        void Image(string rel) { var p = Path.Combine(mods, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllText(p, "x"); }
        void Tags(string rel) { var p = Path.Combine(mods, rel, ImageTagStore.FileName); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllText(p, ""); }
        try
        {
            Image("Assetpacks/packA/flat/a.png");               // images directly: work folder
            Image("Assetpacks/packA/flat/landscape/b.png");     //   ...and its child is classified within it
            Tags("Assetpacks/packB/tagged");                    // tag file, images only in category folders
            Image("Assetpacks/packB/tagged/human/c.jpg");
            Image("Assetpacks/packC/sorted/pov/d.webp");        // untagged hand sort without own images: the category folder itself
            Image("Assetpacks/_generated/Campaign/horse/e.png");// tool folder: skipped
            Image("Assetpacks/packD/unsorted/f.png");           // unsorted: skipped
            Image("Assetpacks/packE/_image_preview.png");       // tool file is no image
            Directory.CreateDirectory(Path.Combine(mods, "Assetpacks/empty/nothing"));
            Image("ModX/Assetpacks/arona/g.png");               // a mod's asset packs
            Image("ModX/Data/h.png");                           // outside Assetpacks: ignored
            Image("_disabled/Assetpacks/x/i.png");              // tool-named mod folder: skipped

            var found = ImageWorkFolders.Find(mods).Select(f => ImageWorkFolders.DisplayName(mods, f)).ToList();
            checks.Test("work folders: tag file or direct images, first match wins, tool folders skipped", () =>
            {
                checks.Equal(string.Join(" | ", new[]
                {
                    "Assetpacks/packA/flat", "Assetpacks/packB/tagged", "Assetpacks/packC/sorted/pov", "ModX/Assetpacks/arona",
                }), string.Join(" | ", found), "Work folders");
            });
        }
        finally
        {
            var root = Path.GetDirectoryName(mods);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
