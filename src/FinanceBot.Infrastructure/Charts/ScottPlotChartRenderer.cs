using System.Globalization;
using FinanceBot.Application.Actors.Charts;
using FinanceBot.Domain.ValueObjects;
using ScottPlot;

namespace FinanceBot.Infrastructure.Charts;

/// <summary>
/// Реализация <see cref="IChartRenderer"/> поверх ScottPlot 5. Чистая (без I/O) — рендерит в PNG <c>byte[]</c>.
/// </summary>
public sealed class ScottPlotChartRenderer : IChartRenderer
{
    public byte[] Render(ChartDataSet data, int width = 1024, int height = 640) => data switch
    {
        CategoryPieData pie => RenderCategoryPie(pie, width, height),
        DailyBarData daily => RenderDailyBar(daily, width, height),
        BucketUtilizationData buckets => RenderBucketUtilization(buckets, width, height),
        SavingsLineData savings => RenderSavingsLine(savings, width, height),
        _ => throw new NotSupportedException($"Unknown chart dataset type: {data.GetType().Name}")
    };

    private static byte[] RenderCategoryPie(CategoryPieData data, int width, int height)
    {
        var plot = new Plot();
        plot.Title(data.Title);

        var nonZero = data.Slices.Where(s => s.Amount > 0m).ToArray();
        if (nonZero.Length == 0)
        {
            plot.Add.Annotation("Нет данных за период", Alignment.MiddleCenter);
            return ToPng(plot, width, height);
        }

        var slices = nonZero.Select(s =>
        {
            var slice = new PieSlice
            {
                Value = (double)s.Amount,
                FillColor = ColorForCategory(s.Category),
                Label = $"{s.Category} ({s.Amount.ToString("0.00", CultureInfo.InvariantCulture)})"
            };
            return slice;
        }).ToList();

        var pie = plot.Add.Pie(slices);
        pie.SliceLabelDistance = 1.2;
        plot.Axes.Frameless();
        plot.HideGrid();

        return ToPng(plot, width, height);
    }

    private static byte[] RenderDailyBar(DailyBarData data, int width, int height)
    {
        var plot = new Plot();
        plot.Title(data.Title);

        if (data.Days.Count == 0)
        {
            plot.Add.Annotation("Нет данных", Alignment.MiddleCenter);
            return ToPng(plot, width, height);
        }

        var positions = Enumerable.Range(0, data.Days.Count).Select(i => (double)i).ToArray();
        var values = data.Days.Select(d => (double)d.Amount).ToArray();
        var labels = data.Days.Select(d => d.Date.ToString("MM-dd", CultureInfo.InvariantCulture)).ToArray();

        var bars = positions.Zip(values, (x, y) => new Bar { Position = x, Value = y, FillColor = Colors.SteelBlue }).ToArray();
        plot.Add.Bars(bars);
        plot.Axes.Bottom.SetTicks(positions, labels);
        plot.Axes.Bottom.Label.Text = "Дата";
        plot.Axes.Left.Label.Text = "Сумма";

        return ToPng(plot, width, height);
    }

    private static byte[] RenderBucketUtilization(BucketUtilizationData data, int width, int height)
    {
        var plot = new Plot();
        plot.Title(data.Title);

        var positions = new double[] { 0, 1, 2 };
        var labels = new[] { "Essentials", "Fun", "Deposit" };
        var spent = new[] { (double)data.SpentEssentials, (double)data.SpentFun, (double)data.SpentDeposit };
        var remaining = new[]
        {
            Math.Max(0.0, (double)(data.AllocationEssentials - data.SpentEssentials)),
            Math.Max(0.0, (double)(data.AllocationFun - data.SpentFun)),
            Math.Max(0.0, (double)(data.AllocationDeposit - data.SpentDeposit))
        };

        var spentBars = positions.Zip(spent, (x, y) => new Bar { Position = x, Value = y, FillColor = Colors.Tomato, Label = "spent" }).ToArray();
        var remBars = positions.Zip(remaining, (x, y) => new Bar { Position = x, Value = y, ValueBase = spent[(int)x], FillColor = Colors.LightGreen, Label = "remaining" }).ToArray();

        plot.Add.Bars(spentBars);
        plot.Add.Bars(remBars);
        plot.Axes.Bottom.SetTicks(positions, labels);
        plot.Axes.Bottom.Label.Text = "Бакет";
        plot.Axes.Left.Label.Text = "Сумма";

        return ToPng(plot, width, height);
    }

    private static byte[] RenderSavingsLine(SavingsLineData data, int width, int height)
    {
        var plot = new Plot();
        plot.Title(data.Title);

        if (data.Points.Count == 0)
        {
            plot.Add.Annotation("Нет данных по накоплениям", Alignment.MiddleCenter);
            return ToPng(plot, width, height);
        }

        var xs = data.Points.Select(p => p.PeriodStart.ToDateTime(TimeOnly.MinValue).ToOADate()).ToArray();
        var ys = data.Points.Select(p => (double)p.Savings).ToArray();
        var scatter = plot.Add.Scatter(xs, ys);
        scatter.MarkerStyle = new MarkerStyle(MarkerShape.FilledCircle, 8);
        scatter.LineStyle = new LineStyle { Color = Colors.SeaGreen, Width = 2 };

        plot.Axes.DateTimeTicksBottom();
        plot.Axes.Bottom.Label.Text = "Период";
        plot.Axes.Left.Label.Text = "Накопления";

        return ToPng(plot, width, height);
    }

    private static byte[] ToPng(Plot plot, int width, int height)
    {
        using var ms = new MemoryStream();
        var image = plot.GetImage(width, height);
        var bytes = image.GetImageBytes(ImageFormat.Png);
        return bytes;
    }

    private static Color ColorForCategory(Category category) => category switch
    {
        Category.Groceries => Colors.LimeGreen,
        Category.DiningOut => Colors.OrangeRed,
        Category.Transport => Colors.SteelBlue,
        Category.Utilities => Colors.Gold,
        Category.Subscriptions => Colors.MediumPurple,
        Category.Entertainment => Colors.HotPink,
        Category.Health => Colors.Crimson,
        Category.Clothing => Colors.Teal,
        Category.Personal => Colors.SlateGray,
        Category.Education => Colors.DarkCyan,
        Category.Gifts => Colors.DeepPink,
        Category.Travel => Colors.RoyalBlue,
        _ => Colors.Gray
    };
}
