using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class EditorFeedbackState
{
	private const int MaxTrackedCompileGroups = 12;
	private static readonly object Sync = new();
	private static readonly Dictionary<string, TrackedCompileGroup> CompileGroups = new( StringComparer.OrdinalIgnoreCase );
	private static long compileSequence;

	[Event( "compile.started" )]
	public static void OnCompileStarted( CompileGroup compiler )
	{
		if ( compiler is null )
			return;

		lock ( Sync )
		{
			if ( !CompileGroups.TryGetValue( compiler.Name, out var tracked ) )
			{
				tracked = new TrackedCompileGroup
				{
					Group = compiler,
					Name = compiler.Name,
					FirstObservedUtc = DateTime.UtcNow
				};

				CompileGroups[compiler.Name] = tracked;
			}

			tracked.Group = compiler;
			tracked.Sequence = ++compileSequence;
			tracked.LastObservedUtc = DateTime.UtcNow;
			TrimCompileGroups();
		}
	}

	public static object DescribeCompileStatus( int maxDiagnostics )
	{
		maxDiagnostics = Clamp( maxDiagnostics, 0, 100 );

		TrackedCompileGroup[] groups;

		lock ( Sync )
		{
			groups = CompileGroups.Values
				.OrderByDescending( x => x.Sequence )
				.ToArray();
		}

		var snapshots = groups
			.Select( x => DescribeCompileGroup( x, maxDiagnostics ) )
			.ToArray();

		return new
		{
			source = "compile.started event observer",
			observedGroupCount = snapshots.Length,
			maxDiagnostics,
			notes = snapshots.Length == 0
				? "No compile events have been observed since the Agent Bridge editor library loaded. Trigger a code hotload or reopen the project to populate compile diagnostics."
				: "",
			groups = snapshots
		};
	}

	public static CompileHealthSnapshot GetCompileHealth()
	{
		TrackedCompileGroup[] groups;

		lock ( Sync )
		{
			groups = CompileGroups.Values
				.OrderByDescending( x => x.Sequence )
				.ToArray();
		}

		if ( groups.Length == 0 )
		{
			return new CompileHealthSnapshot
			{
				ObservedGroupCount = 0,
				LatestSequence = null,
				BuildSucceeded = false,
				IsBuilding = false,
				NeedsBuild = false,
				ErrorCount = 0,
				WarningCount = 0
			};
		}

		var compilerSnapshots = groups
			.SelectMany( group => group.Group.Compilers?.Select( DescribeCompiler ) ?? Array.Empty<CompilerSnapshot>() )
			.ToArray();

		return new CompileHealthSnapshot
		{
			ObservedGroupCount = groups.Length,
			LatestSequence = groups.Max( x => x.Sequence ),
			BuildSucceeded = compilerSnapshots.Length > 0 && compilerSnapshots.All( x => x.BuildSuccess ),
			IsBuilding = groups.Any( x => x.Group.IsBuilding ) || compilerSnapshots.Any( x => x.IsBuilding ),
			NeedsBuild = groups.Any( x => x.Group.NeedsBuild ) || compilerSnapshots.Any( x => x.NeedsBuild ),
			ErrorCount = compilerSnapshots.Sum( x => x.ErrorCount ),
			WarningCount = compilerSnapshots.Sum( x => x.WarningCount )
		};
	}

	public static object DescribeLogs( int maxLines, string contains, string level, int afterIndex = -1 )
	{
		maxLines = Clamp( maxLines, 1, 1000 );
		contains ??= "";
		level = string.IsNullOrWhiteSpace( level ) ? "all" : level.Trim().ToLowerInvariant();
		afterIndex = Math.Max( -1, afterIndex );

		var path = Path.Combine( Environment.CurrentDirectory, "logs", "sbox-dev.log" );
		var exists = File.Exists( path );
		var readError = "";
		var logRead = LogReadResult.Empty;

		if ( exists )
		{
			try
			{
				logRead = ReadTailLines( path, maxLines * 4, afterIndex );
			}
			catch ( Exception ex )
			{
				readError = ex.Message;
			}
		}

		var entries = logRead.Lines
			.Select( ( line, index ) => ParseLogLine( line, logRead.StartIndex + index ) )
			.Where( x => MatchesLogFilter( x, contains, level ) )
			.TakeLast( maxLines )
			.ToArray();

		var returnedNewestIndex = entries.Length == 0 ? (int?)null : entries.Max( x => x.Index );
		var newestIndex = logRead.TotalLineCount <= 0 ? -1 : logRead.TotalLineCount - 1;

		return new
		{
			source = "sbox-dev.log",
			path,
			exists,
			readError,
			maxLines,
			afterIndex,
			newestIndex,
			returnedNewestIndex,
			nextAfterIndex = newestIndex,
			totalLineCount = logRead.TotalLineCount,
			truncatedByTailLimit = logRead.TruncatedByTailLimit,
			contains,
			level,
			levelSource = "inferred from log text; use raw for exact editor output",
			returned = entries.Length,
			entries
		};
	}

	private static object DescribeCompileGroup( TrackedCompileGroup tracked, int maxDiagnostics )
	{
		var group = tracked.Group;
		var compilers = group.Compilers?.ToArray() ?? Array.Empty<Compiler>();
		var compilerSnapshots = compilers.Select( DescribeCompiler ).ToArray();
		var diagnostics = compilers
			.Where( x => x.Diagnostics is not null )
			.SelectMany( x => x.Diagnostics.Select( diagnostic => DescribeDiagnostic( x.Name, diagnostic ) ) )
			.OrderByDescending( x => SeverityRank( x.Severity ) )
			.ThenBy( x => x.FilePath ?? "" )
			.ThenBy( x => x.LineNumber ?? 0 )
			.Take( maxDiagnostics )
			.ToArray();

		return new
		{
			name = tracked.Name,
			sequence = tracked.Sequence,
			firstObservedUtc = tracked.FirstObservedUtc,
			lastObservedUtc = tracked.LastObservedUtc,
			isBuilding = group.IsBuilding,
			needsBuild = group.NeedsBuild,
			buildResult = group.BuildResult.ToString() ?? "",
			buildSuccess = compilers.Length > 0 && compilers.All( x => x.BuildSuccess ),
			errorCount = compilerSnapshots.Sum( x => x.ErrorCount ),
			warningCount = compilerSnapshots.Sum( x => x.WarningCount ),
			diagnosticCount = compilerSnapshots.Sum( x => x.DiagnosticCount ),
			compilers = compilerSnapshots,
			diagnostics
		};
	}

	private static CompilerSnapshot DescribeCompiler( Compiler compiler )
	{
		var diagnostics = compiler.Diagnostics?.ToArray() ?? Array.Empty<Diagnostic>();

		return new CompilerSnapshot
		{
			Name = compiler.Name,
			IsBuilding = compiler.IsBuilding,
			NeedsBuild = compiler.NeedsBuild,
			BuildSuccess = compiler.BuildSuccess,
			BuildResult = compiler.BuildResult.ToString() ?? "",
			DiagnosticCount = diagnostics.Length,
			ErrorCount = diagnostics.Count( x => x.Severity == DiagnosticSeverity.Error ),
			WarningCount = diagnostics.Count( x => x.Severity == DiagnosticSeverity.Warning )
		};
	}

	private static DiagnosticSnapshot DescribeDiagnostic( string compilerName, Diagnostic diagnostic )
	{
		string filePath = null;
		int? lineNumber = null;
		int? charNumber = null;
		var location = diagnostic.Location?.ToString() ?? "";

		try
		{
			var span = diagnostic.Location.GetLineSpan();
			var mappedSpan = diagnostic.Location.GetMappedLineSpan();

			filePath = mappedSpan.HasMappedPath ? mappedSpan.Path : span.Path;
			lineNumber = mappedSpan.Span.Start.Line + 1;
			charNumber = mappedSpan.Span.Start.Character + 1;
		}
		catch
		{
			// Some diagnostics are not tied to a source file.
		}

		return new DiagnosticSnapshot
		{
			Compiler = compilerName,
			Id = diagnostic.Id,
			Severity = diagnostic.Severity.ToString(),
			Message = diagnostic.GetMessage(),
			FilePath = string.IsNullOrWhiteSpace( filePath ) ? null : filePath,
			LineNumber = lineNumber,
			CharNumber = charNumber,
			Location = location
		};
	}

	private static LogReadResult ReadTailLines( string path, int maxLines, int afterIndex )
	{
		maxLines = Clamp( maxLines, 1, 4000 );
		var lines = new Queue<(int Index, string Line)>();
		var totalLineCount = 0;
		var candidateCount = 0;

		using var stream = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete );
		using var reader = new StreamReader( stream );

		while ( reader.ReadLine() is { } line )
		{
			var index = totalLineCount++;
			if ( index <= afterIndex )
				continue;

			candidateCount++;
			if ( lines.Count >= maxLines )
				lines.Dequeue();

			lines.Enqueue( (index, line) );
		}

		return new LogReadResult
		{
			Lines = lines.Select( x => x.Line ).ToArray(),
			StartIndex = lines.Count == 0 ? totalLineCount : lines.Peek().Index,
			TotalLineCount = totalLineCount,
			TruncatedByTailLimit = candidateCount > lines.Count
		};
	}

	private static LogLineSnapshot ParseLogLine( string raw, int index )
	{
		var trimmed = raw.Trim();
		var logger = "";
		var message = trimmed;
		var timestamp = "";
		var isContinuation = true;

		if ( raw.Length >= 24 && raw[4] == '/' && raw[7] == '/' )
		{
			timestamp = raw[..24].Trim();
			isContinuation = false;
		}

		var bracketStart = raw.IndexOf( '[' );
		var bracketEnd = bracketStart >= 0 ? raw.IndexOf( ']', bracketStart + 1 ) : -1;

		if ( bracketStart >= 0 && bracketEnd > bracketStart )
		{
			logger = raw.Substring( bracketStart + 1, bracketEnd - bracketStart - 1 );
			message = raw[(bracketEnd + 1)..].Trim();
		}

		return new LogLineSnapshot
		{
			Index = index,
			Timestamp = timestamp,
			Logger = logger,
			Level = InferLogLevel( raw ),
			LevelInferred = true,
			Message = message,
			Raw = raw,
			IsContinuation = isContinuation
		};
	}

	private static bool MatchesLogFilter( LogLineSnapshot line, string contains, string level )
	{
		if ( !string.IsNullOrWhiteSpace( contains ) && !line.Raw.Contains( contains, StringComparison.OrdinalIgnoreCase ) )
			return false;

		if ( level == "all" )
			return true;

		return string.Equals( line.Level, level, StringComparison.OrdinalIgnoreCase );
	}

	private static string InferLogLevel( string raw )
	{
		if ( raw.Contains( "error", StringComparison.OrdinalIgnoreCase ) ||
			raw.Contains( "exception", StringComparison.OrdinalIgnoreCase ) ||
			raw.Contains( "failed", StringComparison.OrdinalIgnoreCase ) ||
			raw.Contains( "compile failed", StringComparison.OrdinalIgnoreCase ) )
			return "error";

		if ( raw.Contains( "warning", StringComparison.OrdinalIgnoreCase ) ||
			raw.Contains( "warn", StringComparison.OrdinalIgnoreCase ) )
			return "warn";

		if ( raw.Contains( "trace", StringComparison.OrdinalIgnoreCase ) )
			return "trace";

		return "info";
	}

	private static int SeverityRank( string severity )
	{
		return severity switch
		{
			"Error" => 4,
			"Warning" => 3,
			"Info" => 2,
			"Hidden" => 1,
			_ => 0
		};
	}

	private static void TrimCompileGroups()
	{
		if ( CompileGroups.Count <= MaxTrackedCompileGroups )
			return;

		var remove = CompileGroups.Values
			.OrderBy( x => x.Sequence )
			.Take( CompileGroups.Count - MaxTrackedCompileGroups )
			.Select( x => x.Name )
			.ToArray();

		foreach ( var name in remove )
		{
			CompileGroups.Remove( name );
		}
	}

	private static int Clamp( int value, int min, int max )
	{
		if ( value < min )
			return min;

		if ( value > max )
			return max;

		return value;
	}

	private sealed class TrackedCompileGroup
	{
		public CompileGroup Group { get; set; }
		public string Name { get; set; }
		public long Sequence { get; set; }
		public DateTime FirstObservedUtc { get; set; }
		public DateTime LastObservedUtc { get; set; }
	}

	private sealed class CompilerSnapshot
	{
		public string Name { get; set; }
		public bool IsBuilding { get; set; }
		public bool NeedsBuild { get; set; }
		public bool BuildSuccess { get; set; }
		public string BuildResult { get; set; }
		public int DiagnosticCount { get; set; }
		public int ErrorCount { get; set; }
		public int WarningCount { get; set; }
	}

	private sealed class DiagnosticSnapshot
	{
		public string Compiler { get; set; }
		public string Id { get; set; }
		public string Severity { get; set; }
		public string Message { get; set; }
		public string FilePath { get; set; }
		public int? LineNumber { get; set; }
		public int? CharNumber { get; set; }
		public string Location { get; set; }
	}

	private sealed class LogLineSnapshot
	{
		public int Index { get; set; }
		public string Timestamp { get; set; }
		public string Logger { get; set; }
		public string Level { get; set; }
		public bool LevelInferred { get; set; }
		public string Message { get; set; }
		public string Raw { get; set; }
		public bool IsContinuation { get; set; }
	}

	private sealed class LogReadResult
	{
		public static readonly LogReadResult Empty = new()
		{
			Lines = Array.Empty<string>(),
			StartIndex = 0,
			TotalLineCount = 0,
			TruncatedByTailLimit = false
		};

		public string[] Lines { get; set; }
		public int StartIndex { get; set; }
		public int TotalLineCount { get; set; }
		public bool TruncatedByTailLimit { get; set; }
	}

	internal sealed class CompileHealthSnapshot
	{
		public int ObservedGroupCount { get; set; }
		public long? LatestSequence { get; set; }
		public bool BuildSucceeded { get; set; }
		public bool IsBuilding { get; set; }
		public bool NeedsBuild { get; set; }
		public int ErrorCount { get; set; }
		public int WarningCount { get; set; }
	}
}
