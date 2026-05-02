using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Editor;
using Sandbox;
using EditorConsoleSystem = global::Editor.ConsoleSystem;

namespace SboxAgentBridge.Editor;

internal static class ReferenceHandlers
{
	public static BridgeResponse Search( BridgeRequest request )
	{
		var query = HandlerUtil.GetRequiredString( request.Payload, "query" );
		var maxResults = Math.Clamp( HandlerUtil.GetInt( request.Payload, "maxResults", 50 ), 1, 500 );
		var kind = HandlerUtil.GetString( request.Payload, "kind", "all" );
		var comparison = StringComparison.OrdinalIgnoreCase;
		var results = EnumerateDocMembers()
			.Where( entry => MatchesKind( entry.Kind, kind ) )
			.Where( entry => entry.Name.Contains( query, comparison ) || entry.Summary.Contains( query, comparison ) )
			.Take( maxResults )
			.Select( DescribeDocEntry )
			.ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Reference search completed",
			verified = new
			{
				query,
				kind,
				maxResults,
				count = results.Length,
				documentCount = GetXmlDocPaths().Length,
				results
			}
		} );
	}

	public static BridgeResponse Type( BridgeRequest request )
	{
		var typeName = HandlerUtil.GetRequiredString( request.Payload, "typeName" );
		var type = RequireType( typeName );
		var docs = GetDocsForType( type );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Reference type inspected",
			verified = new
			{
				query = typeName,
				type = DescribeType( type, docs )
			}
		} );
	}

	public static BridgeResponse Console( BridgeRequest request )
	{
		var name = HandlerUtil.GetRequiredString( request.Payload, "name" );
		var value = EditorConsoleSystem.GetValue( name, "" );
		var intValue = EditorConsoleSystem.GetValueInt( name, 0 );
		var floatValue = EditorConsoleSystem.GetValueFloat( name, 0f );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Console variable read",
			verified = new
			{
				name,
				value,
				intValue,
				floatValue
			}
		} );
	}

	public static BridgeResponse Whitelist( BridgeRequest request )
	{
		var maxResults = Math.Clamp( HandlerUtil.GetInt( request.Payload, "maxResults", 50 ), 1, 500 );
		var query = HandlerUtil.GetString( request.Payload, "query" );
		var type = FindType( "Sandbox.Internal.TypeLibrary" );
		var property = type?.GetProperty( "WhitelistedSystemMembers", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic );
		var values = new List<string>();
		var readError = "";

		try
		{
			if ( property?.GetValue( null ) is System.Collections.IEnumerable enumerable )
			{
				foreach ( var item in enumerable )
				{
					var text = item?.ToString() ?? "";
					if ( string.IsNullOrWhiteSpace( text ) )
						continue;

					if ( !string.IsNullOrWhiteSpace( query ) && !text.Contains( query, StringComparison.OrdinalIgnoreCase ) )
						continue;

					values.Add( text );
					if ( values.Count >= maxResults )
						break;
				}
			}
		}
		catch ( Exception ex )
		{
			readError = ex.Message;
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "API whitelist inspected",
			verified = new
			{
				available = property is not null && string.IsNullOrWhiteSpace( readError ),
				query,
				maxResults,
				count = values.Count,
				results = values.ToArray(),
				readError
			}
		} );
	}

	private static object DescribeType( Type type, IReadOnlyDictionary<string, DocEntry> docs )
	{
		var bindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
		var declaredFlags = bindingFlags | BindingFlags.DeclaredOnly;

		return new
		{
			name = type.Name,
			fullName = type.FullName ?? type.Name,
			assembly = type.Assembly.GetName().Name ?? "",
			assemblyLocation = Safe( () => type.Assembly.Location, "" ),
			@namespace = type.Namespace ?? "",
			isPublic = type.IsPublic || type.IsNestedPublic,
			isAbstract = type.IsAbstract,
			isEnum = type.IsEnum,
			baseType = type.BaseType?.FullName,
			interfaces = type.GetInterfaces().Select( x => x.FullName ?? x.Name ).OrderBy( x => x, StringComparer.OrdinalIgnoreCase ).ToArray(),
			summary = docs.TryGetValue( GetDocName( type ), out var typeDoc ) ? typeDoc.Summary : "",
			properties = type.GetProperties( bindingFlags )
				.Where( x => x.GetIndexParameters().Length == 0 )
				.OrderBy( x => x.Name, StringComparer.OrdinalIgnoreCase )
				.Select( x => DescribeProperty( x, docs ) )
				.ToArray(),
			methods = type.GetMethods( declaredFlags )
				.Where( x => !x.IsSpecialName )
				.OrderBy( x => x.Name, StringComparer.OrdinalIgnoreCase )
				.Select( x => DescribeMethod( x, docs ) )
				.ToArray(),
			fields = type.GetFields( bindingFlags )
				.OrderBy( x => x.Name, StringComparer.OrdinalIgnoreCase )
				.Select( x => DescribeField( x, docs ) )
				.ToArray(),
			enumValues = type.IsEnum ? Enum.GetNames( type ) : Array.Empty<string>()
		};
	}

	private static object DescribeProperty( PropertyInfo property, IReadOnlyDictionary<string, DocEntry> docs )
	{
		var docName = GetDocName( property );

		return new
		{
			name = property.Name,
			type = property.PropertyType.FullName ?? property.PropertyType.Name,
			canRead = property.CanRead,
			canWrite = property.CanWrite,
			isStatic = (property.GetMethod ?? property.SetMethod)?.IsStatic ?? false,
			summary = docs.TryGetValue( docName, out var doc ) ? doc.Summary : ""
		};
	}

	private static object DescribeMethod( MethodInfo method, IReadOnlyDictionary<string, DocEntry> docs )
	{
		var docName = GetDocName( method );
		var doc = docs.FirstOrDefault( x => x.Key == docName || x.Key.StartsWith( docName + "(", StringComparison.Ordinal ) ).Value;

		return new
		{
			name = method.Name,
			returnType = method.ReturnType.FullName ?? method.ReturnType.Name,
			isStatic = method.IsStatic,
			parameters = method.GetParameters().Select( x => new
			{
				name = x.Name ?? "",
				type = x.ParameterType.FullName ?? x.ParameterType.Name,
				hasDefaultValue = x.HasDefaultValue
			} ).ToArray(),
			summary = doc?.Summary ?? ""
		};
	}

	private static object DescribeField( FieldInfo field, IReadOnlyDictionary<string, DocEntry> docs )
	{
		var docName = GetDocName( field );

		return new
		{
			name = field.Name,
			type = field.FieldType.FullName ?? field.FieldType.Name,
			isStatic = field.IsStatic,
			isLiteral = field.IsLiteral,
			summary = docs.TryGetValue( docName, out var doc ) ? doc.Summary : ""
		};
	}

	private static IReadOnlyDictionary<string, DocEntry> GetDocsForType( Type type )
	{
		var prefix = (type.FullName ?? type.Name).Replace( '+', '.' );
		return EnumerateDocMembers()
			.Where( entry => entry.Name == $"T:{prefix}" || entry.Name.StartsWith( $"P:{prefix}.", StringComparison.Ordinal ) || entry.Name.StartsWith( $"M:{prefix}.", StringComparison.Ordinal ) || entry.Name.StartsWith( $"F:{prefix}.", StringComparison.Ordinal ) )
			.GroupBy( entry => entry.Name )
			.ToDictionary( group => group.Key, group => group.First() );
	}

	private static object DescribeDocEntry( DocEntry entry )
	{
		return new
		{
			name = entry.Name,
			kind = entry.Kind,
			declaringType = entry.DeclaringType,
			member = entry.Member,
			summary = entry.Summary,
			assembly = entry.Assembly,
			sourceFile = entry.SourceFile
		};
	}

	private static bool MatchesKind( string entryKind, string requestedKind )
	{
		if ( string.IsNullOrWhiteSpace( requestedKind ) || string.Equals( requestedKind, "all", StringComparison.OrdinalIgnoreCase ) )
			return true;

		return string.Equals( entryKind, requestedKind, StringComparison.OrdinalIgnoreCase );
	}

	private static IEnumerable<DocEntry> EnumerateDocMembers()
	{
		foreach ( var path in GetXmlDocPaths() )
		{
			XDocument document;
			try
			{
				document = XDocument.Load( path );
			}
			catch
			{
				continue;
			}

			var assembly = document.Root?.Element( "assembly" )?.Element( "name" )?.Value?.Trim() ?? Path.GetFileNameWithoutExtension( path );
			foreach ( var member in document.Root?.Element( "members" )?.Elements( "member" ) ?? Enumerable.Empty<XElement>() )
			{
				var name = member.Attribute( "name" )?.Value ?? "";
				if ( string.IsNullOrWhiteSpace( name ) )
					continue;

				yield return new DocEntry
				{
					Name = name,
					Kind = GetDocKind( name ),
					DeclaringType = GetDeclaringType( name ),
					Member = GetMemberName( name ),
					Summary = NormalizeWhitespace( member.Element( "summary" )?.Value ?? "" ),
					Assembly = assembly,
					SourceFile = path
				};
			}
		}
	}

	private static string[] GetXmlDocPaths()
	{
		var paths = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
		{
			var location = Safe( () => assembly.Location, "" );
			if ( string.IsNullOrWhiteSpace( location ) )
				continue;

			var xmlPath = Path.ChangeExtension( location, ".xml" );
			if ( File.Exists( xmlPath ) )
				paths.Add( xmlPath );
		}

		var engineXml = Path.ChangeExtension( Safe( () => typeof( GameObject ).Assembly.Location, "" ), ".xml" );
		if ( File.Exists( engineXml ) )
			paths.Add( engineXml );

		var toolsXml = Path.ChangeExtension( Safe( () => typeof( EditorConsoleSystem ).Assembly.Location, "" ), ".xml" );
		if ( File.Exists( toolsXml ) )
			paths.Add( toolsXml );

		var managedDirectory = Path.GetDirectoryName( engineXml );
		if ( !string.IsNullOrWhiteSpace( managedDirectory ) && Directory.Exists( managedDirectory ) )
		{
			foreach ( var path in Directory.EnumerateFiles( managedDirectory, "*.xml", SearchOption.TopDirectoryOnly ) )
			{
				paths.Add( path );
			}
		}

		return paths.OrderBy( x => x, StringComparer.OrdinalIgnoreCase ).ToArray();
	}

	private static Type RequireType( string typeName )
	{
		return FindType( typeName ) ?? throw new InvalidOperationException( $"No loaded type matched '{typeName}'." );
	}

	private static Type? FindType( string typeName )
	{
		var matches = new List<Type>();
		foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
		{
			foreach ( var type in GetLoadableTypes( assembly ) )
			{
				if ( string.Equals( type.FullName, typeName, StringComparison.OrdinalIgnoreCase ) ||
					string.Equals( type.Name, typeName, StringComparison.OrdinalIgnoreCase ) )
				{
					matches.Add( type );
				}
			}
		}

		return matches
			.OrderByDescending( type => string.Equals( type.FullName, typeName, StringComparison.OrdinalIgnoreCase ) )
			.ThenByDescending( type => IsSandboxAssembly( type.Assembly ) )
			.ThenByDescending( type => string.Equals( type.FullName, type.Name, StringComparison.OrdinalIgnoreCase ) )
			.ThenBy( type => type.FullName ?? type.Name, StringComparer.OrdinalIgnoreCase )
			.FirstOrDefault();
	}

	private static bool IsSandboxAssembly( Assembly assembly )
	{
		var name = assembly.GetName().Name ?? "";
		return name.StartsWith( "Sandbox", StringComparison.OrdinalIgnoreCase ) || name.StartsWith( "Facepunch", StringComparison.OrdinalIgnoreCase );
	}

	private static IEnumerable<Type> GetLoadableTypes( Assembly assembly )
	{
		try
		{
			return assembly.GetTypes();
		}
		catch ( ReflectionTypeLoadException ex )
		{
			return ex.Types.Where( x => x is not null )!;
		}
		catch
		{
			return Array.Empty<Type>();
		}
	}

	private static string GetDocName( Type type )
	{
		return "T:" + (type.FullName ?? type.Name).Replace( '+', '.' );
	}

	private static string GetDocName( PropertyInfo property )
	{
		return "P:" + ((property.DeclaringType?.FullName ?? "").Replace( '+', '.' )) + "." + property.Name;
	}

	private static string GetDocName( MethodInfo method )
	{
		return "M:" + ((method.DeclaringType?.FullName ?? "").Replace( '+', '.' )) + "." + method.Name;
	}

	private static string GetDocName( FieldInfo field )
	{
		return "F:" + ((field.DeclaringType?.FullName ?? "").Replace( '+', '.' )) + "." + field.Name;
	}

	private static string GetDocKind( string docName )
	{
		return docName.Length < 2 ? "unknown" : docName[0] switch
		{
			'T' => "type",
			'P' => "property",
			'M' => "method",
			'F' => "field",
			'E' => "event",
			_ => "unknown"
		};
	}

	private static string GetDeclaringType( string docName )
	{
		var body = docName.Length > 2 ? docName.Substring( 2 ) : docName;
		var paren = body.IndexOf( '(', StringComparison.Ordinal );
		if ( paren >= 0 )
			body = body.Substring( 0, paren );

		var lastDot = body.LastIndexOf( '.' );
		return lastDot <= 0 ? body : body.Substring( 0, lastDot );
	}

	private static string GetMemberName( string docName )
	{
		var body = docName.Length > 2 ? docName.Substring( 2 ) : docName;
		var paren = body.IndexOf( '(', StringComparison.Ordinal );
		if ( paren >= 0 )
			body = body.Substring( 0, paren );

		var lastDot = body.LastIndexOf( '.' );
		return lastDot <= 0 ? body : body.Substring( lastDot + 1 );
	}

	private static string NormalizeWhitespace( string text )
	{
		return Regex.Replace( text.Trim(), @"\s+", " " );
	}

	private static T Safe<T>( Func<T> read, T fallback )
	{
		try
		{
			return read();
		}
		catch
		{
			return fallback;
		}
	}

	private sealed class DocEntry
	{
		public string Name { get; set; } = "";
		public string Kind { get; set; } = "";
		public string DeclaringType { get; set; } = "";
		public string Member { get; set; } = "";
		public string Summary { get; set; } = "";
		public string Assembly { get; set; } = "";
		public string SourceFile { get; set; } = "";
	}
}
