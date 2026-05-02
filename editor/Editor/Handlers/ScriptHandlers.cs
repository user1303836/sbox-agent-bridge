using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class ScriptHandlers
{
	private static readonly string[] LifecycleMethodNames =
	{
		"OnAwake",
		"OnStart",
		"OnEnabled",
		"OnDisabled",
		"OnUpdate",
		"OnFixedUpdate",
		"OnDestroy",
		"OnValidate"
	};

	public static BridgeResponse Create( BridgeRequest request )
	{
		var relativePath = NormalizeScriptPath( HandlerUtil.GetRequiredString( request.Payload, "path" ) );
		var overwrite = HandlerUtil.GetBool( request.Payload, "overwrite", false );
		var content = HandlerUtil.GetRequiredString( request.Payload, "content" );
		var absolutePath = ResolveCodePath( relativePath );
		var existedBefore = File.Exists( absolutePath );

		if ( existedBefore && !overwrite )
			throw new InvalidOperationException( $"Script '{relativePath}' already exists. Pass overwrite:true to replace it." );

		Directory.CreateDirectory( Path.GetDirectoryName( absolutePath ) ?? Project.Current.GetCodePath() );
		File.WriteAllText( absolutePath, content, new UTF8Encoding( false ) );

		return BridgeResponse.Success( request.Id, new
		{
			message = existedBefore ? "Script overwritten" : "Script created",
			verified = DescribeScript( relativePath )
		} );
	}

	public static BridgeResponse List( BridgeRequest request )
	{
		var query = HandlerUtil.GetString( request.Payload, "query" );
		var maxResults = Math.Clamp( HandlerUtil.GetInt( request.Payload, "maxResults", 200 ), 1, 1000 );
		var codeRoot = Path.GetFullPath( Project.Current.GetCodePath() );
		var results = Directory.Exists( codeRoot )
			? Directory.EnumerateFiles( codeRoot, "*.cs", SearchOption.AllDirectories )
				.Select( path => ToRelativeCodePath( path ) )
				.Where( path => string.IsNullOrWhiteSpace( query ) || path.Contains( query, StringComparison.OrdinalIgnoreCase ) )
				.OrderBy( path => path, StringComparer.OrdinalIgnoreCase )
				.Take( maxResults )
				.Select( DescribeScript )
				.ToArray()
			: Array.Empty<object>();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Scripts listed",
			verified = new
			{
				query,
				maxResults,
				count = results.Length,
				results
			}
		} );
	}

	public static BridgeResponse Read( BridgeRequest request )
	{
		var relativePath = NormalizeScriptPath( HandlerUtil.GetRequiredString( request.Payload, "path" ) );
		var absolutePath = ResolveCodePath( relativePath );
		var maxBytes = Math.Clamp( HandlerUtil.GetInt( request.Payload, "maxBytes", 1024 * 1024 ), 1, 2 * 1024 * 1024 );

		if ( !File.Exists( absolutePath ) )
			throw new InvalidOperationException( $"Script '{relativePath}' does not exist." );

		var bytes = File.ReadAllBytes( absolutePath );
		var truncated = bytes.Length > maxBytes;
		var content = Encoding.UTF8.GetString( truncated ? bytes.Take( maxBytes ).ToArray() : bytes );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Script read",
			verified = new
			{
				script = DescribeScript( relativePath ),
				byteCount = bytes.Length,
				truncated,
				maxBytes,
				content
			}
		} );
	}

	public static BridgeResponse Search( BridgeRequest request )
	{
		var query = HandlerUtil.GetRequiredString( request.Payload, "query" );
		var pathFilter = HandlerUtil.GetString( request.Payload, "path" );
		var caseSensitive = HandlerUtil.GetBool( request.Payload, "caseSensitive", false );
		var maxMatches = Math.Clamp( HandlerUtil.GetInt( request.Payload, "maxMatches", 100 ), 1, 1000 );
		var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
		var codeRoot = Path.GetFullPath( Project.Current.GetCodePath() );
		var matches = Directory.Exists( codeRoot )
			? Directory.EnumerateFiles( codeRoot, "*.cs", SearchOption.AllDirectories )
				.Select( path => new { AbsolutePath = path, RelativePath = ToRelativeCodePath( path ) } )
				.Where( item => string.IsNullOrWhiteSpace( pathFilter ) || item.RelativePath.Contains( pathFilter, StringComparison.OrdinalIgnoreCase ) )
				.SelectMany( item => SearchFile( item.RelativePath, item.AbsolutePath, query, comparison ) )
				.Take( maxMatches )
				.ToArray()
			: Array.Empty<object>();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Scripts searched",
			verified = new
			{
				query,
				path = pathFilter,
				caseSensitive,
				maxMatches,
				count = matches.Length,
				results = matches
			}
		} );
	}

	public static BridgeResponse Analyze( BridgeRequest request )
	{
		var content = HandlerUtil.GetString( request.Payload, "content" );
		var relativePath = "";

		if ( string.IsNullOrWhiteSpace( content ) )
		{
			relativePath = NormalizeScriptPath( HandlerUtil.GetRequiredString( request.Payload, "path" ) );
			var absolutePath = ResolveCodePath( relativePath );

			if ( !File.Exists( absolutePath ) )
				throw new InvalidOperationException( $"Script '{relativePath}' does not exist." );

			content = File.ReadAllText( absolutePath );
		}

		var analysis = AnalyzeSource( content );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Script analyzed",
			verified = new
			{
				path = relativePath,
				lineCount = CountLines( content ),
				analysis
			}
		} );
	}

	public static BridgeResponse Edit( BridgeRequest request )
	{
		var relativePath = NormalizeScriptPath( HandlerUtil.GetRequiredString( request.Payload, "path" ) );
		var content = HandlerUtil.GetRequiredString( request.Payload, "content" );
		var absolutePath = ResolveCodePath( relativePath );

		if ( !File.Exists( absolutePath ) )
			throw new InvalidOperationException( $"Script '{relativePath}' does not exist." );

		var before = DescribeScript( relativePath );
		File.WriteAllText( absolutePath, content, new UTF8Encoding( false ) );
		var after = DescribeScript( relativePath );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Script edited",
			verified = new
			{
				before,
				after
			}
		} );
	}

	public static BridgeResponse Delete( BridgeRequest request )
	{
		var relativePath = NormalizeScriptPath( HandlerUtil.GetRequiredString( request.Payload, "path" ) );
		var absolutePath = ResolveCodePath( relativePath );
		var before = File.Exists( absolutePath ) ? DescribeScript( relativePath ) : null;

		if ( File.Exists( absolutePath ) )
			File.Delete( absolutePath );

		return BridgeResponse.Success( request.Id, new
		{
			message = before is null ? "Script already absent" : "Script deleted",
			verified = new
			{
				path = relativePath,
				existedBefore = before is not null,
				existsAfter = File.Exists( absolutePath ),
				before
			}
		} );
	}

	private static object DescribeScript( string relativePath )
	{
		var absolutePath = ResolveCodePath( relativePath );
		var info = new FileInfo( absolutePath );
		var hash = "";

		if ( info.Exists )
		{
			using var sha = SHA256.Create();
			using var stream = File.OpenRead( absolutePath );
			hash = Convert.ToHexString( sha.ComputeHash( stream ) ).ToLowerInvariant();
		}

		return new
		{
			path = relativePath,
			absolutePath,
			exists = info.Exists,
			length = info.Exists ? info.Length : 0,
			lastWriteUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue,
			sha256 = hash
		};
	}

	private static object[] SearchFile( string relativePath, string absolutePath, string query, StringComparison comparison )
	{
		return File.ReadLines( absolutePath )
			.Select( ( line, index ) => new { line, index } )
			.Where( item => item.line.Contains( query, comparison ) )
			.Select( item => (object)new
			{
				path = relativePath,
				lineNumber = item.index + 1,
				line = item.line.TrimEnd()
			} )
			.ToArray();
	}

	private static object AnalyzeSource( string content )
	{
		var attributes = Regex.Matches( content, @"\[(?<name>[A-Za-z_][\w\.]*(?:\.[A-Za-z_][\w]*)?)" )
			.Select( match => match.Groups["name"].Value )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.OrderBy( name => name, StringComparer.OrdinalIgnoreCase )
			.ToArray();
		var lifecycleMethods = LifecycleMethodNames
			.Where( name => Regex.IsMatch( content, @"\b" + Regex.Escape( name ) + @"\s*\(", RegexOptions.CultureInvariant ) )
			.ToArray();
		var classes = Regex.Matches( content, @"\bclass\s+(?<name>[A-Za-z_]\w*)\s*(?::\s*(?<bases>[^{\r\n]+))?", RegexOptions.CultureInvariant )
			.Select( match =>
			{
				var bases = match.Groups["bases"].Success
					? match.Groups["bases"].Value.Split( ',' ).Select( x => x.Trim() ).Where( x => x.Length > 0 ).ToArray()
					: Array.Empty<string>();

				return new
				{
					name = match.Groups["name"].Value,
					baseTypes = bases,
					isComponent = bases.Any( x => string.Equals( x, "Component", StringComparison.OrdinalIgnoreCase ) || x.EndsWith( ".Component", StringComparison.OrdinalIgnoreCase ) ),
					interfaces = bases.Where( x => x.StartsWith( "I", StringComparison.Ordinal ) ).ToArray()
				};
			} )
			.ToArray();

		return new
		{
			classes,
			attributes,
			lifecycleMethods,
			propertyAttributeCount = CountAttribute( attributes, "Property" ),
			syncAttributeCount = CountAttribute( attributes, "Sync" ),
			rpcAttributeCount = attributes.Count( x => x.StartsWith( "Rpc", StringComparison.OrdinalIgnoreCase ) ),
			containsSceneStartup = content.Contains( "ISceneStartup", StringComparison.Ordinal ),
			containsGameObjectNetworkEvents = content.Contains( "IGameObjectNetworkEvents", StringComparison.Ordinal )
		};
	}

	private static int CountAttribute( string[] attributes, string name )
	{
		return attributes.Count( x => string.Equals( x, name, StringComparison.OrdinalIgnoreCase ) || x.EndsWith( "." + name, StringComparison.OrdinalIgnoreCase ) );
	}

	private static int CountLines( string content )
	{
		if ( string.IsNullOrEmpty( content ) )
			return 0;

		return content.Count( x => x == '\n' ) + 1;
	}

	private static string NormalizeScriptPath( string path )
	{
		path = (path ?? "").Replace( '\\', '/' ).Trim().TrimStart( '/' );

		if ( path.StartsWith( "code/", StringComparison.OrdinalIgnoreCase ) )
			path = path.Substring( "code/".Length );

		if ( string.IsNullOrWhiteSpace( path ) )
			throw new InvalidOperationException( "Script path cannot be empty." );

		if ( !path.EndsWith( ".cs", StringComparison.OrdinalIgnoreCase ) )
			path += ".cs";

		if ( path.Split( '/' ).Any( part => part == ".." ) )
			throw new InvalidOperationException( "Script path cannot contain '..' segments." );

		return path;
	}

	private static string ResolveCodePath( string relativePath )
	{
		var codeRoot = Path.GetFullPath( Project.Current.GetCodePath() );
		var absolutePath = Path.GetFullPath( Path.Combine( codeRoot, relativePath.Replace( '/', Path.DirectorySeparatorChar ) ) );

		if ( !absolutePath.StartsWith( codeRoot, StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidOperationException( "Resolved script path escaped the project Code directory." );

		return absolutePath;
	}

	private static string ToRelativeCodePath( string absolutePath )
	{
		var codeRoot = Path.GetFullPath( Project.Current.GetCodePath() );
		return Path.GetRelativePath( codeRoot, absolutePath ).Replace( '\\', '/' );
	}
}
