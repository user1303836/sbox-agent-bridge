using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class ScriptHandlers
{
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
}
