using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GuildRoster.Client;

internal sealed class LuaTable
{
    private readonly Dictionary<string, object?> _fields = new(StringComparer.Ordinal);
    private readonly List<object?> _values = new();

    public IReadOnlyDictionary<string, object?> Fields => _fields;
    public IReadOnlyList<object?> Values => _values;

    internal void SetField(string key, object? value) => _fields[key] = value;
    internal void AddValue(object? value) => _values.Add(value);

    public bool TryGetTable(string key, out LuaTable table)
    {
        if (_fields.TryGetValue(key, out var value) && value is LuaTable found)
        {
            table = found;
            return true;
        }

        table = null!;
        return false;
    }

    public string? GetString(string key)
    {
        if (!_fields.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text => text,
            long integer => integer.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString(CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            _ => null,
        };
    }

    public int? GetInt(string key)
    {
        if (!_fields.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            long integer when integer >= int.MinValue && integer <= int.MaxValue => (int)integer,
            double number when number >= int.MinValue && number <= int.MaxValue => (int)Math.Round(number),
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        if (!_fields.TryGetValue(key, out var value) || value is null)
        {
            return defaultValue;
        }

        return value switch
        {
            bool boolean => boolean,
            long integer => integer != 0,
            double number => Math.Abs(number) > double.Epsilon,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => defaultValue,
        };
    }
}

internal static class LuaSavedVariablesParser
{
    public static LuaTable ParseAssignment(string text, string variableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);

        var pattern = $@"(?m)^\s*{Regex.Escape(variableName)}\s*=\s*";
        var match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            throw new InvalidDataException($"SavedVariables assignment '{variableName}' was not found.");
        }

        var parser = new Parser(text, match.Index + match.Length);
        return parser.ParseRootTable();
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _position;

        public Parser(string text, int startPosition)
        {
            _text = text;
            _position = startPosition;
        }

        public LuaTable ParseRootTable()
        {
            SkipTrivia();
            var value = ParseValue();
            if (value is not LuaTable table)
            {
                throw Error("Expected a Lua table as the assignment value.");
            }

            return table;
        }

        private object? ParseValue()
        {
            SkipTrivia();
            if (_position >= _text.Length)
            {
                throw Error("Unexpected end of SavedVariables data.");
            }

            var current = _text[_position];
            if (current == '{')
            {
                return ParseTable();
            }

            if (current is '"' or '\'')
            {
                return ParseString();
            }

            if (current == '-' || current == '+' || char.IsDigit(current))
            {
                return ParseNumber();
            }

            var identifier = ParseIdentifier();
            return identifier switch
            {
                "true" => true,
                "false" => false,
                "nil" => null,
                _ => throw Error($"Unsupported bare Lua value '{identifier}'."),
            };
        }

        private LuaTable ParseTable()
        {
            Expect('{');
            var table = new LuaTable();

            while (true)
            {
                SkipTrivia();
                if (TryConsume('}'))
                {
                    return table;
                }

                string? key = null;
                var keyed = false;

                if (TryConsume('['))
                {
                    var keyValue = ParseValue();
                    SkipTrivia();
                    Expect(']');
                    SkipTrivia();
                    Expect('=');
                    key = ConvertKey(keyValue);
                    keyed = true;
                }
                else
                {
                    var savedPosition = _position;
                    if (TryParseIdentifier(out var identifier))
                    {
                        SkipTrivia();
                        if (TryConsume('='))
                        {
                            key = identifier;
                            keyed = true;
                        }
                        else
                        {
                            _position = savedPosition;
                        }
                    }
                }

                var value = ParseValue();
                if (keyed)
                {
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        throw Error("Lua table key was empty or unsupported.");
                    }
                    table.SetField(key, value);
                }
                else
                {
                    table.AddValue(value);
                }

                SkipTrivia();
                if (TryConsume(',') || TryConsume(';'))
                {
                    continue;
                }

                SkipTrivia();
                if (_position < _text.Length && _text[_position] == '}')
                {
                    continue;
                }

                throw Error("Expected ',', ';', or '}' after Lua table entry.");
            }
        }

        private string ParseString()
        {
            var quote = _text[_position++];
            var builder = new StringBuilder();

            while (_position < _text.Length)
            {
                var current = _text[_position++];
                if (current == quote)
                {
                    return builder.ToString();
                }

                if (current != '\\')
                {
                    builder.Append(current);
                    continue;
                }

                if (_position >= _text.Length)
                {
                    throw Error("Unterminated escape sequence in Lua string.");
                }

                var escaped = _text[_position++];
                switch (escaped)
                {
                    case 'a': builder.Append('\a'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'v': builder.Append('\v'); break;
                    case '\\': builder.Append('\\'); break;
                    case '"': builder.Append('"'); break;
                    case '\'': builder.Append('\''); break;
                    case '\n': break;
                    case '\r':
                        if (_position < _text.Length && _text[_position] == '\n')
                        {
                            _position++;
                        }
                        break;
                    case 'z':
                        while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
                        {
                            _position++;
                        }
                        break;
                    case 'x':
                        builder.Append((char)ParseHexEscape(2));
                        break;
                    default:
                        if (char.IsDigit(escaped))
                        {
                            builder.Append((char)ParseDecimalEscape(escaped));
                        }
                        else
                        {
                            builder.Append(escaped);
                        }
                        break;
                }
            }

            throw Error("Unterminated Lua string.");
        }

        private int ParseHexEscape(int digits)
        {
            if (_position + digits > _text.Length)
            {
                throw Error("Incomplete hexadecimal escape in Lua string.");
            }

            var span = _text.AsSpan(_position, digits);
            if (!int.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                throw Error("Invalid hexadecimal escape in Lua string.");
            }

            _position += digits;
            return value;
        }

        private int ParseDecimalEscape(char firstDigit)
        {
            Span<char> digits = stackalloc char[3];
            digits[0] = firstDigit;
            var count = 1;
            while (count < digits.Length && _position < _text.Length && char.IsDigit(_text[_position]))
            {
                digits[count++] = _text[_position++];
            }

            if (!int.TryParse(digits[..count], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value > 255)
            {
                throw Error("Invalid decimal escape in Lua string.");
            }

            return value;
        }

        private object ParseNumber()
        {
            var start = _position;
            if (_text[_position] is '+' or '-')
            {
                _position++;
            }

            while (_position < _text.Length && char.IsDigit(_text[_position]))
            {
                _position++;
            }

            var isFloatingPoint = false;
            if (_position < _text.Length && _text[_position] == '.')
            {
                isFloatingPoint = true;
                _position++;
                while (_position < _text.Length && char.IsDigit(_text[_position]))
                {
                    _position++;
                }
            }

            if (_position < _text.Length && _text[_position] is 'e' or 'E')
            {
                isFloatingPoint = true;
                _position++;
                if (_position < _text.Length && _text[_position] is '+' or '-')
                {
                    _position++;
                }
                while (_position < _text.Length && char.IsDigit(_text[_position]))
                {
                    _position++;
                }
            }

            var token = _text.AsSpan(start, _position - start);
            if (!isFloatingPoint && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            {
                return integer;
            }

            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            throw Error($"Invalid Lua number '{token.ToString()}'.");
        }

        private string ParseIdentifier()
        {
            if (!TryParseIdentifier(out var identifier))
            {
                throw Error("Expected Lua value.");
            }

            return identifier;
        }

        private bool TryParseIdentifier(out string identifier)
        {
            SkipTrivia();
            var start = _position;
            if (_position >= _text.Length || !(_text[_position] == '_' || char.IsLetter(_text[_position])))
            {
                identifier = string.Empty;
                return false;
            }

            _position++;
            while (_position < _text.Length && (_text[_position] == '_' || char.IsLetterOrDigit(_text[_position])))
            {
                _position++;
            }

            identifier = _text[start.._position];
            return true;
        }

        private void SkipTrivia()
        {
            while (_position < _text.Length)
            {
                if (char.IsWhiteSpace(_text[_position]))
                {
                    _position++;
                    continue;
                }

                if (_position + 1 < _text.Length && _text[_position] == '-' && _text[_position + 1] == '-')
                {
                    _position += 2;
                    if (_position + 1 < _text.Length && _text[_position] == '[' && _text[_position + 1] == '[')
                    {
                        _position += 2;
                        var end = _text.IndexOf("]]", _position, StringComparison.Ordinal);
                        _position = end >= 0 ? end + 2 : _text.Length;
                    }
                    else
                    {
                        while (_position < _text.Length && _text[_position] is not '\r' and not '\n')
                        {
                            _position++;
                        }
                    }
                    continue;
                }

                break;
            }
        }

        private bool TryConsume(char expected)
        {
            SkipTrivia();
            if (_position < _text.Length && _text[_position] == expected)
            {
                _position++;
                return true;
            }

            return false;
        }

        private void Expect(char expected)
        {
            if (!TryConsume(expected))
            {
                throw Error($"Expected '{expected}'.");
            }
        }

        private static string ConvertKey(object? value) => value switch
        {
            string text => text,
            long integer => integer.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString(CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            _ => throw new InvalidDataException("Unsupported Lua table key type."),
        };

        private InvalidDataException Error(string message) =>
            new($"{message} Position {_position:N0} in SavedVariables text.");
    }
}
