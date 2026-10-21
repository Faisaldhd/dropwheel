using System.Windows;
using FileAction.App;
using FileAction.App.UI;
using FileAction.App.Wheel;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// The concrete labels the label audit found clipped. Each panel is measured exactly as the wheel
/// measures it, then its desired box is checked against the same slice geometry the wheel uses.
/// </summary>
public class LabelFitVerificationTests
{
    [Theory]
    [InlineData("Edit metadata", "Tags and chapters", 8, 1)]
    [InlineData("Edit metadata", "Tags and chapters", 6, 1)]
    [InlineData("Merge to PDF", null, 8, 6)]
    public void Outer_english_audit_labels_fit(string label, string? subtitle, int count, int index) =>
        RunSta(() =>
        {
            AssertFits(new FakeOperation("audit-en", label, subtitle), count, index);
        });

    [Theory]
    [InlineData("تعديل البيانات الوصفية", "الوسوم والفصول", 12, 1)]
    [InlineData("رفع عائم", "احتفظ به لوقت لاحق", 6, 4)]
    [InlineData("رفع عائم", "احتفظ به لوقت لاحق", 5, 4)]
    [InlineData("دمج ملفات PDF", null, 8, 6)]
    public void Outer_arabic_audit_labels_fit(string label, string? subtitle, int count, int index) =>
        RunSta(() =>
        {
            LanguageManager.Apply(UiLanguage.Arabic);
            try
            {
                AssertFits(new FakeOperation("audit-ar", label, subtitle), count, index);
            }
            finally
            {
                LanguageManager.Apply(UiLanguage.English);
            }
        });

    [Theory]
    [InlineData("Organize Pages", 6, 0)]
    [InlineData("Remove BG", 6, 5)]
    [InlineData("Redact video", 5, 2)]
    public void Inner_audit_labels_fit(string label, int count, int index) =>
        RunSta(() =>
        {
            var op = new FakeOperation("audit-inner", label, null);
            var width = WheelWindow.InnerSliceLabelWidth(count, index);
            var panel = WheelWindow.SliceLabel(op, false, width, WheelWindow.InnerLabelMaxHeight);
            panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.True(panel.DesiredSize.Width <= width + 1,
                $"{label}: width {panel.DesiredSize.Width:0.0} > {width:0.0}");
            Assert.True(WheelWindow.LabelBoxFits(
                    count, index, WheelWindow.InnerInnerRadius, WheelWindow.InnerOuterRadius,
                    WheelWindow.InnerLabelRadius, panel.DesiredSize.Width, panel.DesiredSize.Height),
                $"{label}: inner box leaves the band");
        });

    private static void AssertFits(Operation op, int count, int index)
    {
        var width = WheelWindow.OuterSliceLabelWidth(count, index);
        var panel = WheelWindow.SliceLabel(op, false, width, WheelWindow.OuterLabelMaxHeight);
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.True(panel.DesiredSize.Width <= width + 1,
            $"{op.Label}: width {panel.DesiredSize.Width:0.0} > {width:0.0}");
        Assert.True(panel.DesiredSize.Height <= WheelWindow.OuterLabelMaxHeight + 1,
            $"{op.Label}: height {panel.DesiredSize.Height:0.0} > {WheelWindow.OuterLabelMaxHeight:0.0}");
        Assert.True(WheelWindow.LabelBoxFits(
                count, index, WheelWindow.OuterInnerRadius, WheelWindow.OuterRadius,
                WheelWindow.OuterLabelRadius, panel.DesiredSize.Width, panel.DesiredSize.Height),
            $"{op.Label}: {count} slices, slice {index}, {panel.DesiredSize.Width:0.0}×{panel.DesiredSize.Height:0.0} leaves the band");
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw failure;
    }

    private sealed class FakeOperation(string id, string label, string? subtitle)
        : Operation(id, WheelKind.Tools, label, "x", subtitle)
    {
        public override bool AppliesTo(IReadOnlyList<FileItem> items) => true;

        public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
            Task.FromResult(OperationResult.Of([]));
    }
}
