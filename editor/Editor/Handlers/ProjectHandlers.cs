using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class ProjectHandlers
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true
	};

	public static BridgeResponse ListFiles( BridgeRequest request )
	{
		var root = ResolveRoot( HandlerUtil.GetString( request.Payload, "root", "assets" ) );
		var glob = HandlerUtil.GetString( request.Payload, "glob", "*" );
		var recursive = HandlerUtil.GetBool( request.Payload, "recursive", true );
		var includeDirectories = HandlerUtil.GetBool( request.Payload, "includeDirectories", false );
		var maxResults = Math.Clamp( HandlerUtil.GetInt( request.Payload, "maxResults", 200 ), 1, 2000 );
		var search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

		var entries = Directory.Exists( root.AbsolutePath )
			? Directory.EnumerateFileSystemEntries( root.AbsolutePath, glob, search )
				.Where( x => includeDirectories || File.Exists( x ) )
				.OrderBy( x => ToRelativePath( root.AbsolutePath, x ), StringComparer.OrdinalIgnoreCase )
				.Take( maxResults )
				.Select( x => DescribePath( root, x ) )
				.ToArray()
			: Array.Empty<object>();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Project files listed",
			verified = new
			{
				root = root.Key,
				rootPath = root.AbsolutePath,
				glob,
				recursive,
				includeDirectories,
				count = entries.Length,
				maxResults,
				results = entries
			}
		} );
	}

	public static BridgeResponse ReadFile( BridgeRequest request )
	{
		var resolved = ResolveFile( request.Payload );
		var maxBytes = Math.Clamp( HandlerUtil.GetInt( request.Payload, "maxBytes", 1024 * 1024 ), 1, 2 * 1024 * 1024 );

		if ( !File.Exists( resolved.AbsolutePath ) )
			throw new InvalidOperationException( $"Project file '{resolved.RelativePath}' does not exist under root '{resolved.Root.Key}'." );

		var bytes = File.ReadAllBytes( resolved.AbsolutePath );
		var truncated = bytes.Length > maxBytes;
		var slice = truncated ? bytes.Take( maxBytes ).ToArray() : bytes;
		var isText = IsProbablyText( slice );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Project file read",
			verified = new
			{
				file = DescribePath( resolved.Root, resolved.AbsolutePath ),
				byteCount = bytes.Length,
				truncated,
				maxBytes,
				isText,
				content = isText ? Encoding.UTF8.GetString( slice ) : "",
				base64 = isText ? "" : Convert.ToBase64String( slice )
			}
		} );
	}

	public static BridgeResponse WriteFile( BridgeRequest request )
	{
		var resolved = ResolveFile( request.Payload, allowMissing: true );
		var content = HandlerUtil.GetRequiredString( request.Payload, "content" );
		var overwrite = HandlerUtil.GetBool( request.Payload, "overwrite", false );
		var createDirectories = HandlerUtil.GetBool( request.Payload, "createDirectories", true );
		var existedBefore = File.Exists( resolved.AbsolutePath );

		if ( existedBefore && !overwrite )
			throw new InvalidOperationException( $"Project file '{resolved.RelativePath}' already exists. Pass overwrite:true to replace it." );

		var directory = Path.GetDirectoryName( resolved.AbsolutePath );
		if ( createDirectories && !string.IsNullOrWhiteSpace( directory ) )
			Directory.CreateDirectory( directory );

		File.WriteAllText( resolved.AbsolutePath, content, new UTF8Encoding( false ) );

		return BridgeResponse.Success( request.Id, new
		{
			message = existedBefore ? "Project file overwritten" : "Project file written",
			verified = new
			{
				existedBefore,
				file = DescribePath( resolved.Root, resolved.AbsolutePath )
			}
		} );
	}

	public static BridgeResponse DeleteFile( BridgeRequest request )
	{
		var resolved = ResolveFile( request.Payload, allowMissing: true );
		var before = File.Exists( resolved.AbsolutePath ) ? DescribePath( resolved.Root, resolved.AbsolutePath ) : null;

		if ( File.Exists( resolved.AbsolutePath ) )
			File.Delete( resolved.AbsolutePath );

		return BridgeResponse.Success( request.Id, new
		{
			message = before is null ? "Project file already absent" : "Project file deleted",
			verified = new
			{
				root = resolved.Root.Key,
				path = resolved.RelativePath,
				existedBefore = before is not null,
				existsAfter = File.Exists( resolved.AbsolutePath ),
				before
			}
		} );
	}

	public static BridgeResponse InputActions( BridgeRequest request )
	{
		var query = HandlerUtil.GetString( request.Payload, "query" );
		var groupName = HandlerUtil.GetString( request.Payload, "groupName" );
		var config = ReadInputConfig();
		var actions = GetInputActions( config.Root );
		var results = actions
			.OfType<JsonObject>()
			.Select( DescribeInputAction )
			.Where( x =>
				(string.IsNullOrWhiteSpace( query ) || Contains( x.Name, query ) || Contains( x.Title, query )) &&
				(string.IsNullOrWhiteSpace( groupName ) || string.Equals( x.GroupName, groupName, StringComparison.OrdinalIgnoreCase ))
			)
			.OrderBy( x => x.GroupName, StringComparer.OrdinalIgnoreCase )
			.ThenBy( x => x.Name, StringComparer.OrdinalIgnoreCase )
			.ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Input actions inspected",
			verified = new
			{
				config = DescribePath( config.RootInfo, config.Path ),
				query,
				groupName,
				total = actions.Count,
				count = results.Length,
				results
			}
		} );
	}

	public static BridgeResponse UpsertInputAction( BridgeRequest request )
	{
		var name = HandlerUtil.GetRequiredString( request.Payload, "name" );
		var config = ReadInputConfig();
		var actions = GetInputActions( config.Root );
		var existing = actions
			.OfType<JsonObject>()
			.FirstOrDefault( x => string.Equals( ReadString( x, "Name" ), name, StringComparison.OrdinalIgnoreCase ) );
		var existedBefore = existing is not null;
		var before = existing is null ? null : DescribeInputAction( existing );
		var action = existing ?? new JsonObject
		{
			["Name"] = name,
			["GroupName"] = "Agent Bridge",
			["Title"] = null,
			["KeyboardCode"] = "None",
			["GamepadCode"] = "None"
		};

		action["Name"] = name;
		SetOptionalString( request.Payload, action, "groupName", "GroupName" );
		SetOptionalStringOrNull( request.Payload, action, "title", "Title" );
		SetOptionalString( request.Payload, action, "keyboardCode", "KeyboardCode" );
		SetOptionalString( request.Payload, action, "gamepadCode", "GamepadCode" );

		if ( existing is null )
			actions.Add( action );

		WriteInputConfig( config.Path, config.Root );

		var after = DescribeInputAction( action );

		return BridgeResponse.Success( request.Id, new
		{
			message = existedBefore ? "Input action updated" : "Input action created",
			verified = new
			{
				config = DescribePath( config.RootInfo, config.Path ),
				existedBefore,
				before,
				after
			}
		} );
	}

	public static BridgeResponse RemoveInputAction( BridgeRequest request )
	{
		var name = HandlerUtil.GetRequiredString( request.Payload, "name" );
		var config = ReadInputConfig();
		var actions = GetInputActions( config.Root );
		JsonObject? removed = null;

		for ( var i = actions.Count - 1; i >= 0; i-- )
		{
			if ( actions[i] is not JsonObject action )
				continue;

			if ( !string.Equals( ReadString( action, "Name" ), name, StringComparison.OrdinalIgnoreCase ) )
				continue;

			removed = action;
			actions.RemoveAt( i );
		}

		WriteInputConfig( config.Path, config.Root );

		return BridgeResponse.Success( request.Id, new
		{
			message = removed is null ? "Input action already absent" : "Input action removed",
			verified = new
			{
				config = DescribePath( config.RootInfo, config.Path ),
				name,
				existedBefore = removed is not null,
				existsAfter = actions.OfType<JsonObject>().Any( x => string.Equals( ReadString( x, "Name" ), name, StringComparison.OrdinalIgnoreCase ) ),
				before = removed is null ? null : DescribeInputAction( removed )
			}
		} );
	}

	private static InputConfig ReadInputConfig()
	{
		var root = ResolveRoot( "settings" );
		var path = Path.Combine( root.AbsolutePath, "Input.config" );

		if ( !File.Exists( path ) )
			throw new InvalidOperationException( "ProjectSettings/Input.config does not exist in the active project." );

		var json = JsonNode.Parse( File.ReadAllText( path ) ) as JsonObject;
		if ( json is null )
			throw new InvalidOperationException( "ProjectSettings/Input.config did not parse as a JSON object." );

		return new InputConfig( root, path, json );
	}

	private static JsonArray GetInputActions( JsonObject root )
	{
		if ( root["Actions"] is JsonArray actions )
			return actions;

		actions = new JsonArray();
		root["Actions"] = actions;
		return actions;
	}

	private static void WriteInputConfig( string path, JsonObject root )
	{
		File.WriteAllText( path, root.ToJsonString( JsonOptions ) + Environment.NewLine, new UTF8Encoding( false ) );
	}

	private static InputActionDescription DescribeInputAction( JsonObject action )
	{
		return new InputActionDescription(
			ReadString( action, "Name" ),
			ReadString( action, "GroupName" ),
			ReadString( action, "Title" ),
			ReadString( action, "KeyboardCode" ),
			ReadString( action, "GamepadCode" )
		);
	}

	private static ProjectRoot ResolveRoot( string root )
	{
		var projectRoot = Path.GetFullPath( Project.Current.GetRootPath() );
		var normalized = string.IsNullOrWhiteSpace( root ) ? "assets" : root.Trim().ToLowerInvariant();
		var path = normalized switch
		{
			"project" or "root" => projectRoot,
			"assets" => Project.Current.GetAssetsPath(),
			"code" => Project.Current.GetCodePath(),
			"editor" => Project.Current.GetEditorPath(),
			"settings" or "projectsettings" or "project_settings" => Path.Combine( projectRoot, "ProjectSettings" ),
			_ => throw new InvalidOperationException( $"Unsupported project file root '{root}'. Use project, assets, code, editor, or settings." )
		};

		return new ProjectRoot( normalized, Path.GetFullPath( path ) );
	}

	private static ResolvedProjectFile ResolveFile( JsonElement payload, bool allowMissing = false )
	{
		var root = ResolveRoot( HandlerUtil.GetString( payload, "root", "assets" ) );
		var relativePath = NormalizeRelativePath( HandlerUtil.GetRequiredString( payload, "path" ) );
		var absolutePath = Path.GetFullPath( Path.Combine( root.AbsolutePath, relativePath.Replace( '/', Path.DirectorySeparatorChar ) ) );

		EnsureInsideRoot( root.AbsolutePath, absolutePath );

		if ( !allowMissing && !File.Exists( absolutePath ) )
			throw new InvalidOperationException( $"Project file '{relativePath}' does not exist under root '{root.Key}'." );

		return new ResolvedProjectFile( root, relativePath, absolutePath );
	}

	private static string NormalizeRelativePath( string path )
	{
		path = (path ?? "").Replace( '\\', '/' ).Trim().TrimStart( '/' );

		if ( string.IsNullOrWhiteSpace( path ) )
			throw new InvalidOperationException( "Project file path cannot be empty." );

		if ( path.Split( '/' ).Any( part => part == ".." ) )
			throw new InvalidOperationException( "Project file path cannot contain '..' segments." );

		return path;
	}

	private static void EnsureInsideRoot( string rootPath, string absolutePath )
	{
		var fullRoot = Path.GetFullPath( rootPath ).TrimEnd( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar );
		var rootPrefix = fullRoot + Path.DirectorySeparatorChar;
		var fullPath = Path.GetFullPath( absolutePath );

		if ( !string.Equals( fullPath, fullRoot, StringComparison.OrdinalIgnoreCase ) && !fullPath.StartsWith( rootPrefix, StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidOperationException( "Resolved project file path escaped the selected project root." );
	}

	private static object DescribePath( ProjectRoot root, string absolutePath )
	{
		var isDirectory = Directory.Exists( absolutePath );
		var isFile = File.Exists( absolutePath );
		var info = isFile ? new FileInfo( absolutePath ) : null;

		return new
		{
			root = root.Key,
			path = ToRelativePath( root.AbsolutePath, absolutePath ),
			absolutePath,
			exists = isDirectory || isFile,
			kind = isDirectory ? "directory" : "file",
			length = info?.Length ?? 0,
			lastWriteUtc = isDirectory || isFile ? File.GetLastWriteTimeUtc( absolutePath ) : DateTime.MinValue,
			sha256 = isFile ? Sha256( absolutePath ) : ""
		};
	}

	private static string ToRelativePath( string rootPath, string absolutePath )
	{
		return Path.GetRelativePath( rootPath, absolutePath ).Replace( '\\', '/' );
	}

	private static bool IsProbablyText( byte[] bytes )
	{
		return !bytes.Any( x => x == 0 );
	}

	private static string Sha256( string absolutePath )
	{
		using var sha = SHA256.Create();
		using var stream = File.OpenRead( absolutePath );
		return Convert.ToHexString( sha.ComputeHash( stream ) ).ToLowerInvariant();
	}

	private static bool Contains( string value, string query )
	{
		return value?.IndexOf( query ?? "", StringComparison.OrdinalIgnoreCase ) >= 0;
	}

	private static string ReadString( JsonObject obj, string name )
	{
		return obj.TryGetPropertyValue( name, out var value ) ? value?.GetValue<string>() ?? "" : "";
	}

	private static void SetOptionalString( JsonElement payload, JsonObject obj, string payloadName, string jsonName )
	{
		if ( payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty( payloadName, out var value ) && value.ValueKind == JsonValueKind.String )
			obj[jsonName] = value.GetString() ?? "";
	}

	private static void SetOptionalStringOrNull( JsonElement payload, JsonObject obj, string payloadName, string jsonName )
	{
		if ( payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty( payloadName, out var value ) )
			return;

		obj[jsonName] = value.ValueKind == JsonValueKind.Null ? null : value.GetString() ?? "";
	}

	private sealed record ProjectRoot( string Key, string AbsolutePath );
	private sealed record ResolvedProjectFile( ProjectRoot Root, string RelativePath, string AbsolutePath );
	private sealed record InputConfig( ProjectRoot RootInfo, string Path, JsonObject Root );
	private sealed record InputActionDescription( string Name, string GroupName, string Title, string KeyboardCode, string GamepadCode );
}
