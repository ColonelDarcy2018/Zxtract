using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using ExtractUtil.App.ViewModels;

namespace ExtractUtil.App.Controls;

// A lightweight view of job states, never a second queue or a byte-progress estimate.
public sealed class TaskStateStrip : FrameworkElement
{
    private readonly HashSet<JobItemViewModel> _observed = new();
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(TaskStateStrip),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, SourceChanged));
    public static readonly DependencyProperty WorkflowIndexProperty = DependencyProperty.Register(
        nameof(WorkflowIndex), typeof(int), typeof(TaskStateStrip), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PendingBrushProperty = BrushProperty(nameof(PendingBrush));
    public static readonly DependencyProperty RunningBrushProperty = BrushProperty(nameof(RunningBrush));
    public static readonly DependencyProperty CompletedBrushProperty = BrushProperty(nameof(CompletedBrush));
    public static readonly DependencyProperty AttentionBrushProperty = BrushProperty(nameof(AttentionBrush));

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public int WorkflowIndex { get => (int)GetValue(WorkflowIndexProperty); set => SetValue(WorkflowIndexProperty, value); }
    public Brush PendingBrush { get => (Brush)GetValue(PendingBrushProperty); set => SetValue(PendingBrushProperty, value); }
    public Brush RunningBrush { get => (Brush)GetValue(RunningBrushProperty); set => SetValue(RunningBrushProperty, value); }
    public Brush CompletedBrush { get => (Brush)GetValue(CompletedBrushProperty); set => SetValue(CompletedBrushProperty, value); }
    public Brush AttentionBrush { get => (Brush)GetValue(AttentionBrushProperty); set => SetValue(AttentionBrushProperty, value); }

    public TaskStateStrip()
    {
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach(ItemsSource);
    }

    private static DependencyProperty BrushProperty(string name) => DependencyProperty.Register(name, typeof(Brush),
        typeof(TaskStateStrip), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    private static void SourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var strip = (TaskStateStrip)d;
        strip.Detach(e.OldValue as IEnumerable);
        strip.Attach();
    }

    private void Attach()
    {
        if (ItemsSource is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged -= JobsChanged;
            collection.CollectionChanged += JobsChanged;
        }
        ObserveRows();
    }

    private void Detach(IEnumerable? source)
    {
        if (source is INotifyCollectionChanged collection) collection.CollectionChanged -= JobsChanged;
        foreach (var row in _observed) row.PropertyChanged -= RowChanged;
        _observed.Clear();
    }

    private void ObserveRows()
    {
        var current = ItemsSource?.OfType<JobItemViewModel>().ToHashSet() ?? new HashSet<JobItemViewModel>();
        foreach (var row in _observed.Except(current).ToArray())
        {
            row.PropertyChanged -= RowChanged;
            _observed.Remove(row);
        }
        foreach (var row in current.Except(_observed))
        {
            row.PropertyChanged += RowChanged;
            _observed.Add(row);
        }
        InvalidateVisual();
    }

    private void JobsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset) { ObserveRows(); return; }
        if (e.OldItems is not null)
            foreach (var row in e.OldItems.OfType<JobItemViewModel>())
                if (_observed.Remove(row)) row.PropertyChanged -= RowChanged;
        if (e.NewItems is not null)
            foreach (var row in e.NewItems.OfType<JobItemViewModel>())
                if (_observed.Add(row)) row.PropertyChanged += RowChanged;
        InvalidateVisual();
    }
    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(JobItemViewModel.Status) or nameof(JobItemViewModel.Phase)) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var rows = ItemsSource?.OfType<JobItemViewModel>().Where(row => row.IsTreeItem == (WorkflowIndex == 1)).ToArray();
        if (rows is null || rows.Length == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        // At large queue sizes aggregate by physical width; attention takes priority so errors remain visible.
        var segments = Math.Min(rows.Length, Math.Max(1, (int)(ActualWidth / 4)));
        var step = ActualWidth / segments;
        for (var i = 0; i < segments; i++)
        {
            var start = i * rows.Length / segments;
            var end = (i + 1) * rows.Length / segments;
            var group = rows.AsSpan(start, end - start);
            var brush = CompletedBrush;
            foreach (var row in group)
            {
                if (row.NeedsAttention) { brush = AttentionBrush; break; }
                if (row.IsRunning) brush = RunningBrush;
                else if (!row.IsCompleted && brush != RunningBrush) brush = PendingBrush;
            }
            drawingContext.DrawRoundedRectangle(brush, null, new Rect(i * step, 0, Math.Max(1, step - 2), ActualHeight), 2, 2);
        }
    }
}
