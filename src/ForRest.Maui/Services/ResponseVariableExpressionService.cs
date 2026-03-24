using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForRest.Maui.Services;

public static class ResponseVariableExpressionService
{
	private static readonly HashSet<string> ReservedRootMembers =
	[
		"status",
		"body",
		"contenttype",
		"headers",
		"snapshot",
		"json"
	];

	public static bool TryBuildExpression(string? jsonText, int lineNumber, int column, out string expression)
	{
		expression = string.Empty;
		if (string.IsNullOrWhiteSpace(jsonText))
		{
			return false;
		}

		int targetIndex = TryResolveTargetIndex(jsonText, lineNumber, column);
		if (targetIndex < 0)
		{
			return false;
		}

		try
		{
			Parser parser = new(jsonText, targetIndex);
			if (!parser.TryFindPath(out IReadOnlyList<PathSegment>? path) || path is null || path.Count == 0)
			{
				return false;
			}

			expression = BuildExpression(path);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static int TryResolveTargetIndex(string text, int lineNumber, int column)
	{
		if (lineNumber < 1 || column < 1)
		{
			return -1;
		}

		int currentLine = 1;
		int lineStart = 0;
		for (int index = 0; index < text.Length; index++)
		{
			if (currentLine == lineNumber)
			{
				break;
			}

			if (text[index] == '\n')
			{
				currentLine++;
				lineStart = index + 1;
			}
		}

		if (currentLine != lineNumber)
		{
			return -1;
		}

		int lineEnd = text.IndexOf('\n', lineStart);
		if (lineEnd < 0)
		{
			lineEnd = text.Length;
		}

		if (lineEnd <= lineStart)
		{
			return -1;
		}

		int requestedIndex = lineStart + column - 1;
		return Math.Clamp(requestedIndex, lineStart, lineEnd - 1);
	}

	private static string BuildExpression(IReadOnlyList<PathSegment> path)
	{
		StringBuilder builder = new("response");
		for (int index = 0; index < path.Count; index++)
		{
			PathSegment segment = path[index];
			if (segment.ArrayIndex is int arrayIndex)
			{
				builder.Append('[').Append(arrayIndex).Append(']');
				continue;
			}

			string propertyName = segment.PropertyName ?? string.Empty;
			bool preferBracketNotation = !IsIdentifier(propertyName)
				|| (index == 0 && ReservedRootMembers.Contains(NormalizeMemberName(propertyName)));
			if (preferBracketNotation)
			{
				builder.Append("[\"")
					.Append(propertyName.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal))
					.Append("\"]");
				continue;
			}

			builder.Append('.').Append(propertyName);
		}

		return builder.ToString();
	}

	private static bool IsIdentifier(string value)
	{
		if (string.IsNullOrWhiteSpace(value) || !(char.IsLetter(value[0]) || value[0] == '_'))
		{
			return false;
		}

		for (int index = 1; index < value.Length; index++)
		{
			if (!(char.IsLetterOrDigit(value[index]) || value[index] == '_'))
			{
				return false;
			}
		}

		return true;
	}

	private static string NormalizeMemberName(string value)
	{
		StringBuilder builder = new(value.Length);
		foreach (char character in value)
		{
			if (char.IsLetterOrDigit(character))
			{
				builder.Append(char.ToLowerInvariant(character));
			}
		}

		return builder.ToString();
	}

	private readonly record struct PathSegment(string? PropertyName, int? ArrayIndex)
	{
		public static PathSegment Property(string propertyName) => new(propertyName, null);

		public static PathSegment Index(int arrayIndex) => new(null, arrayIndex);
	}

	private sealed class Parser(string text, int targetIndex)
	{
		private readonly string _text = text;
		private readonly int _targetIndex = targetIndex;
		private int _position;

		public bool TryFindPath(out IReadOnlyList<PathSegment>? path)
		{
			_position = 0;
			SkipWhitespace();
			return TryParseValue([], out path);
		}

		private bool TryParseValue(IReadOnlyList<PathSegment> currentPath, out IReadOnlyList<PathSegment>? path)
		{
			path = null;
			if (_position >= _text.Length)
			{
				return false;
			}

			return _text[_position] switch
			{
				'{' => TryParseObject(currentPath, out path),
				'[' => TryParseArray(currentPath, out path),
				'"' => ConsumeStringValue(),
				_ => ParseScalar()
			};
		}

		private bool ConsumeStringValue()
		{
			TryParseString(out _, out _, out _);
			return false;
		}

		private bool TryParseObject(IReadOnlyList<PathSegment> currentPath, out IReadOnlyList<PathSegment>? path)
		{
			path = null;
			_position++;
			SkipWhitespace();
			if (TryConsume('}'))
			{
				return false;
			}

			while (_position < _text.Length)
			{
				SkipWhitespace();
				string? propertyName = TryParseString(out int rawStart, out int rawEndExclusive, out _);
				if (propertyName is null)
				{
					return false;
				}

				List<PathSegment> propertyPath = [.. currentPath, PathSegment.Property(propertyName)];
				if (_targetIndex >= rawStart && _targetIndex < rawEndExclusive)
				{
					path = propertyPath;
					return true;
				}

				SkipWhitespace();
				if (!TryConsume(':'))
				{
					return false;
				}

				SkipWhitespace();
				if (TryParseValue(propertyPath, out path))
				{
					return true;
				}

				SkipWhitespace();
				if (TryConsume(','))
				{
					continue;
				}

				return TryConsume('}');
			}

			return false;
		}

		private bool TryParseArray(IReadOnlyList<PathSegment> currentPath, out IReadOnlyList<PathSegment>? path)
		{
			path = null;
			_position++;
			SkipWhitespace();
			if (TryConsume(']'))
			{
				return false;
			}

			int arrayIndex = 0;
			while (_position < _text.Length)
			{
				List<PathSegment> itemPath = [.. currentPath, PathSegment.Index(arrayIndex)];
				if (TryParseValue(itemPath, out path))
				{
					return true;
				}

				arrayIndex++;
				SkipWhitespace();
				if (TryConsume(','))
				{
					continue;
				}

				return TryConsume(']');
			}

			return false;
		}

		private string? TryParseString(out int rawStart, out int rawEndExclusive, out int closingQuoteIndex)
		{
			rawStart = -1;
			rawEndExclusive = -1;
			closingQuoteIndex = -1;
			if (!TryConsume('"'))
			{
				return null;
			}

			rawStart = _position;
			StringBuilder builder = new();
			while (_position < _text.Length)
			{
				char current = _text[_position++];
				if (current == '"')
				{
					rawEndExclusive = _position - 1;
					closingQuoteIndex = _position - 1;
					return builder.ToString();
				}

				if (current != '\\')
				{
					builder.Append(current);
					continue;
				}

				if (_position >= _text.Length)
				{
					return null;
				}

				char escape = _text[_position++];
				builder.Append(escape switch
				{
					'"' => '"',
					'\\' => '\\',
					'/' => '/',
					'b' => '\b',
					'f' => '\f',
					'n' => '\n',
					'r' => '\r',
					't' => '\t',
					'u' when TryParseUnicodeEscape(out char unicode) => unicode,
					_ => escape
				});
			}

			return null;
		}

		private bool TryParseUnicodeEscape(out char value)
		{
			value = default;
			if (_position + 4 > _text.Length)
			{
				return false;
			}

			ReadOnlySpan<char> hex = _text.AsSpan(_position, 4);
			if (!ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort codePoint))
			{
				return false;
			}

			_position += 4;
			value = (char)codePoint;
			return true;
		}

		private bool ParseScalar()
		{
			while (_position < _text.Length)
			{
				char current = _text[_position];
				if (current is ',' or '}' or ']' || char.IsWhiteSpace(current))
				{
					break;
				}

				_position++;
			}

			return false;
		}

		private void SkipWhitespace()
		{
			while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
			{
				_position++;
			}
		}

		private bool TryConsume(char expected)
		{
			if (_position >= _text.Length || _text[_position] != expected)
			{
				return false;
			}

			_position++;
			return true;
		}
	}
}
