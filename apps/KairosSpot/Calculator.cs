using System;
using System.Globalization;

namespace KairosSpot
{
    // Evaluador minimo de expresiones aritmeticas (+ - * / % ^ y parentesis).
    // Sin dependencias externas. Parser recursivo descendente.
    public static class Calculator
    {
        public static bool TryEval(string input, out double result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;

            // Solo intentar si parece una expresion (digitos y operadores).
            bool hasDigit = false, hasOp = false;
            foreach (var c in input)
            {
                if (char.IsDigit(c)) hasDigit = true;
                else if ("+-*/%^()".IndexOf(c) >= 0) hasOp = true;
                else if (c != ' ' && c != '.' && c != ',') return false;
            }
            if (!hasDigit || !hasOp) return false;

            try
            {
                var p = new Parser(input.Replace(",", "."));
                result = p.Parse();
                return !double.IsNaN(result) && !double.IsInfinity(result);
            }
            catch
            {
                return false;
            }
        }

        private class Parser
        {
            private readonly string _s;
            private int _pos;

            public Parser(string s) { _s = s; _pos = 0; }

            public double Parse()
            {
                var v = ParseExpr();
                SkipWs();
                if (_pos != _s.Length) throw new FormatException();
                return v;
            }

            private void SkipWs() { while (_pos < _s.Length && _s[_pos] == ' ') _pos++; }

            private double ParseExpr()
            {
                var v = ParseTerm();
                while (true)
                {
                    SkipWs();
                    if (_pos >= _s.Length) break;
                    var c = _s[_pos];
                    if (c == '+') { _pos++; v += ParseTerm(); }
                    else if (c == '-') { _pos++; v -= ParseTerm(); }
                    else break;
                }
                return v;
            }

            private double ParseTerm()
            {
                var v = ParsePow();
                while (true)
                {
                    SkipWs();
                    if (_pos >= _s.Length) break;
                    var c = _s[_pos];
                    if (c == '*') { _pos++; v *= ParsePow(); }
                    else if (c == '/') { _pos++; v /= ParsePow(); }
                    else if (c == '%') { _pos++; v %= ParsePow(); }
                    else break;
                }
                return v;
            }

            private double ParsePow()
            {
                var v = ParseFactor();
                SkipWs();
                if (_pos < _s.Length && _s[_pos] == '^')
                {
                    _pos++;
                    v = Math.Pow(v, ParsePow());
                }
                return v;
            }

            private double ParseFactor()
            {
                SkipWs();
                if (_pos >= _s.Length) throw new FormatException();
                var c = _s[_pos];
                if (c == '(')
                {
                    _pos++;
                    var v = ParseExpr();
                    SkipWs();
                    if (_pos >= _s.Length || _s[_pos] != ')') throw new FormatException();
                    _pos++;
                    return v;
                }
                if (c == '-') { _pos++; return -ParseFactor(); }
                if (c == '+') { _pos++; return ParseFactor(); }

                int start = _pos;
                while (_pos < _s.Length && (char.IsDigit(_s[_pos]) || _s[_pos] == '.')) _pos++;
                if (_pos == start) throw new FormatException();
                return double.Parse(_s.Substring(start, _pos - start), CultureInfo.InvariantCulture);
            }
        }
    }
}
