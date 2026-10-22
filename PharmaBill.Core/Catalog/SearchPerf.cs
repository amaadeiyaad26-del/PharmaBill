using System.Diagnostics;

namespace PharmaBill.Core.Catalog;

/// <summary>Per-request timings of one medicine search. Phases are measured with Stopwatch timestamps.</summary>
public sealed class SearchPerfTrace(string box, long requestId, string query)
{
	private long _dbTicks;

	private long _convertTicks;

	public string Box { get; } = box;

	public long RequestId { get; } = requestId;

	public string Query { get; } = query;

	public double DebounceMs { get; set; }

	public double UiUpdateMs { get; set; }

	public int ResultCount { get; set; }

	public string Outcome { get; set; } = "ok";

	/// <summary>How the drug bank was searched: <see cref="FullTextIndex"/>, or a plain name scan when the index failed or had no hit.</summary>
	public string CatalogLookup { get; set; } = string.Empty;

	public const string FullTextIndex = "full-text index";

	public const string NameScanAfterIndexError = "name scan (full-text index failed)";

	public const string NameScanAfterNoHit = "name scan (no full-text hit)";

	public double DbQueryMs => Stopwatch.GetElapsedTime(0, _dbTicks).TotalMilliseconds;

	public double ConvertMs => Stopwatch.GetElapsedTime(0, _convertTicks).TotalMilliseconds;

	public static long Start() => Stopwatch.GetTimestamp();

	public void AddDb(long startTimestamp) => _dbTicks += Stopwatch.GetTimestamp() - startTimestamp;

	public void AddConvert(long startTimestamp) => _convertTicks += Stopwatch.GetTimestamp() - startTimestamp;
}

public sealed record SearchPerfSample(DateTimeOffset At, string Box, long RequestId, string Query, double DebounceMs, double DbQueryMs, double ConvertMs, double UiUpdateMs, int ResultCount, string Outcome)
{
	public double TotalMs => DebounceMs + DbQueryMs + ConvertMs + UiUpdateMs;
}

/// <summary>Bounded in-memory history of search timings plus an optional sink (the app logs it under the "SearchPerf" tag).</summary>
public static class SearchPerf
{
	private const int Capacity = 200;

	private static readonly object Gate = new object();

	private static readonly Queue<SearchPerfSample> Samples = new Queue<SearchPerfSample>(Capacity);

	public static Action<SearchPerfSample>? Sink { get; set; }

	public static event Action? Recorded;

	public static void Record(SearchPerfTrace trace)
	{
		SearchPerfSample sample = new SearchPerfSample(DateTimeOffset.Now, trace.Box, trace.RequestId, trace.Query, trace.DebounceMs, trace.DbQueryMs, trace.ConvertMs, trace.UiUpdateMs, trace.ResultCount, trace.Outcome);
		lock (Gate)
		{
			if (Samples.Count == Capacity)
			{
				Samples.Dequeue();
			}

			Samples.Enqueue(sample);
		}

		Sink?.Invoke(sample);
		Recorded?.Invoke();
	}

	public static IReadOnlyList<SearchPerfSample> Recent()
	{
		lock (Gate)
		{
			return Samples.ToArray();
		}
	}

	public static void Clear()
	{
		lock (Gate)
		{
			Samples.Clear();
		}
	}
}
