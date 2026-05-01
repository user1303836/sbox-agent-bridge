using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Editor;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class OrientationOverrideStore
{
	private const int CurrentVersion = 1;
	public const string RelativePath = "agent_bridge/orientation_overrides.json";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	public static string AbsolutePath => Path.Combine( Project.Current.GetAssetsPath(), RelativePath.Replace( '/', Path.DirectorySeparatorChar ) );

	public static string NormalizeModelPath( string path )
	{
		path = (path ?? "").Replace( '\\', '/' ).Trim().TrimStart( '/' );

		if ( path.StartsWith( "assets/", StringComparison.OrdinalIgnoreCase ) )
			path = path["assets/".Length..];

		if ( string.IsNullOrWhiteSpace( path ) )
			throw new InvalidOperationException( "Model path cannot be empty." );

		if ( path.Split( '/' ).Any( part => part == ".." ) )
			throw new InvalidOperationException( "Model path cannot contain '..' segments." );

		return path;
	}

	public static OrientationOverrideRecord? Get( string modelPath )
	{
		var file = Load();
		var normalized = NormalizeModelPath( modelPath );

		return file.Models.TryGetValue( normalized, out var record ) ? record : null;
	}

	public static OrientationOverrideRecord Set( OrientationOverrideRecord record )
	{
		record.ModelPath = NormalizeModelPath( record.ModelPath );
		record.UpdatedUtc = DateTime.UtcNow;

		var file = Load();
		file.Models[record.ModelPath] = record;
		Save( file );

		return record;
	}

	public static object DescribeStorage()
	{
		return new
		{
			relativePath = RelativePath,
			absolutePath = AbsolutePath,
			exists = File.Exists( AbsolutePath )
		};
	}

	public static object DescribeRecord( OrientationOverrideRecord record )
	{
		return new
		{
			modelPath = record.ModelPath,
			baseRotation = record.BaseRotation,
			groundOffsetZ = record.GroundOffsetZ,
			forwardAxis = record.ForwardAxis,
			confidence = record.Confidence,
			source = record.Source,
			notes = record.Notes,
			updatedUtc = record.UpdatedUtc
		};
	}

	private static OrientationOverrideFile Load()
	{
		if ( !File.Exists( AbsolutePath ) )
			return new OrientationOverrideFile();

		try
		{
			var file = JsonSerializer.Deserialize<OrientationOverrideFile>( File.ReadAllText( AbsolutePath, Encoding.UTF8 ), JsonOptions )
				?? new OrientationOverrideFile();

			return new OrientationOverrideFile
			{
				Version = file.Version <= 0 ? CurrentVersion : file.Version,
				Models = new Dictionary<string, OrientationOverrideRecord>( file.Models ?? new(), StringComparer.OrdinalIgnoreCase )
			};
		}
		catch ( Exception ex )
		{
			throw new InvalidOperationException( $"Could not read orientation override file '{AbsolutePath}': {ex.Message}", ex );
		}
	}

	private static void Save( OrientationOverrideFile file )
	{
		file.Version = CurrentVersion;
		Directory.CreateDirectory( Path.GetDirectoryName( AbsolutePath ) ?? Project.Current.GetAssetsPath() );

		var json = JsonSerializer.Serialize( file, JsonOptions );
		var tempPath = $"{AbsolutePath}.{Guid.NewGuid():N}.tmp";
		File.WriteAllText( tempPath, json, new UTF8Encoding( false ) );

		if ( File.Exists( AbsolutePath ) )
			File.Delete( AbsolutePath );

		File.Move( tempPath, AbsolutePath );
	}

	internal sealed class OrientationOverrideFile
	{
		public OrientationOverrideFile()
		{
		}

		public int Version { get; set; } = CurrentVersion;
		public Dictionary<string, OrientationOverrideRecord> Models { get; set; } = new( StringComparer.OrdinalIgnoreCase );
	}
}

internal sealed class OrientationOverrideRecord
{
	public OrientationOverrideRecord()
	{
	}

	public string ModelPath { get; set; } = "";
	public OrientationAngles BaseRotation { get; set; } = new();
	public float GroundOffsetZ { get; set; }
	public string ForwardAxis { get; set; } = "+Y";
	public string Confidence { get; set; } = "unverified";
	public string Source { get; set; } = "agent";
	public string Notes { get; set; } = "";
	public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class OrientationAngles
{
	public OrientationAngles()
	{
	}

	public float Pitch { get; set; }
	public float Yaw { get; set; }
	public float Roll { get; set; }
}
