using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Transcriber;

public static class PowerPointExport
{
    public static void Save(string path, IEnumerable<SummaryBlock> blocks)
    {
        var material = blocks.ToList();
        if (material.Count == 0 || material.Any(x => x.Slides.Count == 0)) throw new InvalidOperationException("Generate slide outlines for the selected summary blocks first.");
        using var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentation = doc.AddPresentationPart();
        presentation.Presentation = new P.Presentation();
        var master = presentation.AddNewPart<SlideMasterPart>();
        var layout = master.AddNewPart<SlideLayoutPart>();
        layout.SlideLayout = new P.SlideLayout(new P.CommonSlideData(Tree()), new P.ColorMapOverride(new A.MasterColorMapping())) { Type = P.SlideLayoutValues.Blank, Preserve = true };
        layout.AddPart(master);
        master.SlideMaster = new P.SlideMaster(new P.CommonSlideData(Tree()), new P.ColorMap { Background1 = A.ColorSchemeIndexValues.Light1, Text1 = A.ColorSchemeIndexValues.Dark1, Background2 = A.ColorSchemeIndexValues.Light2, Text2 = A.ColorSchemeIndexValues.Dark2, Accent1 = A.ColorSchemeIndexValues.Accent1, Accent2 = A.ColorSchemeIndexValues.Accent2, Accent3 = A.ColorSchemeIndexValues.Accent3, Accent4 = A.ColorSchemeIndexValues.Accent4, Accent5 = A.ColorSchemeIndexValues.Accent5, Accent6 = A.ColorSchemeIndexValues.Accent6, Hyperlink = A.ColorSchemeIndexValues.Hyperlink, FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink }, new P.SlideLayoutIdList(new P.SlideLayoutId { Id = 2147483649U, RelationshipId = master.GetIdOfPart(layout) }), new P.TextStyles(new P.TitleStyle(), new P.BodyStyle(), new P.OtherStyle()));
        var theme = master.AddNewPart<ThemePart>();
        theme.Theme = new A.Theme(new A.ThemeElements(
            new A.ColorScheme(new A.Dark1Color(new A.RgbColorModelHex { Val = "142B3A" }), new A.Light1Color(new A.RgbColorModelHex { Val = "FFFFFF" }), new A.Dark2Color(new A.RgbColorModelHex { Val = "315162" }), new A.Light2Color(new A.RgbColorModelHex { Val = "F3F7F8" }), new A.Accent1Color(new A.RgbColorModelHex { Val = "147D92" }), new A.Accent2Color(new A.RgbColorModelHex { Val = "DBA344" }), new A.Accent3Color(new A.RgbColorModelHex { Val = "518A70" }), new A.Accent4Color(new A.RgbColorModelHex { Val = "785E9B" }), new A.Accent5Color(new A.RgbColorModelHex { Val = "CB6E64" }), new A.Accent6Color(new A.RgbColorModelHex { Val = "68859B" }), new A.Hyperlink(new A.RgbColorModelHex { Val = "147D92" }), new A.FollowedHyperlinkColor(new A.RgbColorModelHex { Val = "785E9B" })) { Name = "Quillvora" },
            new A.FontScheme(new A.MajorFont(new A.LatinFont { Typeface = "Aptos Display" }, new A.EastAsianFont { Typeface = "" }, new A.ComplexScriptFont { Typeface = "" }), new A.MinorFont(new A.LatinFont { Typeface = "Aptos" }, new A.EastAsianFont { Typeface = "" }, new A.ComplexScriptFont { Typeface = "" })) { Name = "Quillvora" },
            new A.FormatScheme(new A.FillStyleList(Fill("FFFFFF"), Fill("F3F7F8"), Fill("147D92")), new A.LineStyleList(Outline(), Outline(), Outline()), new A.EffectStyleList(new A.EffectStyle(new A.EffectList()), new A.EffectStyle(new A.EffectList()), new A.EffectStyle(new A.EffectList())), new A.BackgroundFillStyleList(Fill("FFFFFF"), Fill("F3F7F8"), Fill("147D92"))) { Name = "Quillvora" })) { Name = "Quillvora" };
        var ids = new P.SlideIdList();
        presentation.Presentation.Append(new P.SlideMasterIdList(new P.SlideMasterId { Id = 2147483648U, RelationshipId = presentation.GetIdOfPart(master) }), ids, new P.SlideSize { Cx = 12192000, Cy = 6858000, Type = P.SlideSizeValues.Screen16x9 }, new P.NotesSize { Cx = 6858000, Cy = 9144000 });
        int number = 0;
        foreach (var block in material)
        foreach (var content in block.Slides)
        {
            number++;
            var slidePart = presentation.AddNewPart<SlidePart>(); slidePart.AddPart(layout);
            var tree = Tree();
            tree.Append(TextBox(2, "Section", .65, .35, 12, .35, block.Title.Length > 100 ? block.Title[..100] : block.Title, 12, "147D92", false));
            tree.Append(TextBox(3, "Heading", .65, .9, 12, 1.1, content.Heading, 30, "142B3A", true));
            for (int i = 0; i < content.Points.Count; i++) tree.Append(TextBox((uint)(4 + i), "Point " + (i + 1), .8, 2.05 + i * .9, 11.8, .87, "•  " + content.Points[i], content.Points[i].Length > 150 ? 18 : 21, "315162", false));
            tree.Append(TextBox(20, "Footer", .65, 7.05, 12, .25, $"Quillvora   ·   Transcript through {TranscriptLine.Time(block.ThroughSeconds)}                                        {number:00}", 10, "68859B", false));
            slidePart.Slide = new P.Slide(new P.CommonSlideData(new P.Background(new P.BackgroundProperties(Fill("FFFFFF"), new A.EffectList())), tree), new P.ColorMapOverride(new A.MasterColorMapping()));
            ids.Append(new P.SlideId { Id = (uint)(255 + number), RelationshipId = presentation.GetIdOfPart(slidePart) });
            slidePart.Slide.Save();
        }
        layout.SlideLayout.Save(); master.SlideMaster.Save(); theme.Theme.Save(); presentation.Presentation.Save();
    }
    private static A.SolidFill Fill(string hex) => new(new A.RgbColorModelHex { Val = hex });
    private static A.Outline Outline() => new(Fill("147D92"), new A.PresetDash { Val = A.PresetLineDashValues.Solid }) { Width = 9525 };
    private static P.ShapeTree Tree() => new(new P.NonVisualGroupShapeProperties(new P.NonVisualDrawingProperties { Id = 1, Name = "" }, new P.NonVisualGroupShapeDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()), new P.GroupShapeProperties(new A.TransformGroup(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = 0, Cy = 0 }, new A.ChildOffset { X = 0, Y = 0 }, new A.ChildExtents { Cx = 0, Cy = 0 })));
    private static P.Shape TextBox(uint id, string name, double x, double y, double w, double h, string text, int size, string color, bool bold) => new(
        new P.NonVisualShapeProperties(new P.NonVisualDrawingProperties { Id = id, Name = name }, new P.NonVisualShapeDrawingProperties { TextBox = true }, new P.ApplicationNonVisualDrawingProperties()),
        new P.ShapeProperties(new A.Transform2D(new A.Offset { X = (long)(x * 914400), Y = (long)(y * 914400) }, new A.Extents { Cx = (long)(w * 914400), Cy = (long)(h * 914400) }), new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }, new A.NoFill()),
        new P.TextBody(new A.BodyProperties { Wrap = A.TextWrappingValues.Square, LeftInset = 0, RightInset = 0, TopInset = 0, BottomInset = 0 }, new A.ListStyle(), new A.Paragraph(new A.Run(new A.RunProperties(Fill(color), new A.LatinFont { Typeface = "Aptos" }) { Language = "en-US", FontSize = size * 100, Bold = bold }, new A.Text(text)), new A.EndParagraphRunProperties { Language = "en-US" })));
}
