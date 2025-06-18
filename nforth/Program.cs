using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ForthConsole
{
    class Program
    {
        static Stack<object> dataStack = new Stack<object>();
        static Stack<object> returnStack = new Stack<object>();
        static Stack<int> beginStack = new Stack<int>();
        static Stack<LoopContext> loopStack = new Stack<LoopContext>();
        static Dictionary<string, List<string>> definitions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        static Dictionary<string, Action> words = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);

        static void Main(string[] args)
        {
            InitializeWords();
            Console.WriteLine("Forth Interpreter Ready. Type \"STOP\" or Ctrl+C to exit.");
            while (true)
            {
                Console.Write("> ");
                var line = Console.ReadLine();
                if (line == null || line.Trim().Equals("STOP", StringComparison.OrdinalIgnoreCase)) break;
                try { Evaluate(line); }
                catch (Exception ex) { Console.WriteLine("Error: " + ex.Message); }
            }
        }

        static void Evaluate(string input)
        {
            input = input.Replace('\t', ' ');
            var tokens = Tokenize(input);
            for (int ip = 0; ip < tokens.Count; ip++)
            {
                var token = tokens[ip];

                // Definition
                if (string.Equals(token, ":", StringComparison.OrdinalIgnoreCase))
                {
                    ip++;
                    var name = tokens[ip];
                    var body = new List<string>();
                    ip++;
                    while (ip < tokens.Count && !tokens[ip].Equals(";"))
                    {
                        body.Add(tokens[ip]);
                        ip++;
                    }
                    definitions[name] = body;
                    continue;
                }

                // User-defined word
                if (definitions.ContainsKey(token))
                {
                    Evaluate(string.Join(" ", definitions[token]));
                    continue;
                }

                // Control flow words
                switch (token.ToUpperInvariant())
                {
                    case "IF": HandleIf(tokens, ref ip); continue;
                    case "ELSE": SkipTo(tokens, ref ip, "THEN"); continue;
                    case "THEN": continue;
                    case "DO":
                        {
                            int start = Convert.ToInt32(Pop());
                            int limit = Convert.ToInt32(Pop());
                            loopStack.Push(new LoopContext { Index = start, Limit = limit, BeginIp = ip });
                            continue;
                        }
                    case "LOOP":
                        {
                            if (loopStack.Count == 0) throw new InvalidOperationException("LOOP without DO");
                            var ctx = loopStack.Peek();
                            ctx.Index++;
                            if (ctx.Index < ctx.Limit)
                                ip = ctx.BeginIp;
                            else
                                loopStack.Pop();
                            continue;
                        }
                    case "+LOOP":
                        {
                            if (loopStack.Count == 0) throw new InvalidOperationException("+LOOP without DO");
                            int n = Convert.ToInt32(Pop());
                            var ctx = loopStack.Peek();
                            ctx.Index += n;
                            bool cont = (n > 0 && ctx.Index < ctx.Limit) || (n < 0 && ctx.Index > ctx.Limit);
                            if (cont)
                                ip = ctx.BeginIp;
                            else
                                loopStack.Pop();
                            continue;
                        }
                    case "I": dataStack.Push(loopStack.Count > 0 ? (object)loopStack.Peek().Index : 0); continue;
                    case "J":
                        {
                            if (loopStack.Count < 2) throw new InvalidOperationException("J requires nested DO");
                            var arr = loopStack.ToArray();
                            dataStack.Push(arr[1].Index);
                            continue;
                        }
                    case "LEAVE":
                        {
                            if (loopStack.Count == 0) throw new InvalidOperationException("LEAVE without DO");
                            loopStack.Pop();
                            ip = FindMatching(tokens, ip, "DO", "LOOP", "+LOOP");
                            continue;
                        }
                    case "BEGIN": beginStack.Push(ip); continue;
                    case "UNTIL":
                        {
                            int flag = Convert.ToInt32(Pop());
                            int beginIp = beginStack.Peek();
                            if (flag == 0) ip = beginIp; else beginStack.Pop();
                            continue;
                        }
                    case "WHILE":
                        {
                            int flag = Convert.ToInt32(Pop());
                            if (flag == 0) ip = FindMatching(tokens, ip, "WHILE", "REPEAT");
                            continue;
                        }
                    case "REPEAT":
                        {
                            if (beginStack.Count == 0) throw new InvalidOperationException("REPEAT without BEGIN");
                            ip = beginStack.Peek();
                            continue;
                        }
                    case "STOP": Environment.Exit(0); continue;
                    case "END": continue;
                }

                // String literal
                if (token.StartsWith("\"") && token.EndsWith("\"") && token.Length >= 2)
                {
                    dataStack.Push(token.Substring(1, token.Length - 2));
                    continue;
                }

                // Date/time literal
                var dateFormats = new[] { "yyyy-MM-dd", "yyyy/M/d", "yyyy-M-d", "yyyy/MM/dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" };
                if (DateTime.TryParseExact(token, dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    dataStack.Push(dt);
                    continue;
                }

                // Number literal
                if (double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out double num))
                {
                    dataStack.Push(num);
                    continue;
                }

                // Built-in word
                if (words.TryGetValue(token, out var action))
                {
                    action();
                    continue;
                }

                throw new InvalidOperationException($"Unknown token: {token}");
            }
        }

        static List<string> Tokenize(string input)
        {
            var list = new List<string>();
            bool inQuote = false;
            var buf = string.Empty;
            foreach (var ch in input)
            {
                if (ch == '"') { inQuote = !inQuote; buf += ch; if (!inQuote) { list.Add(buf); buf = string.Empty; } continue; }
                if (!inQuote && char.IsWhiteSpace(ch)) { if (buf.Length > 0) { list.Add(buf); buf = string.Empty; } }
                else buf += ch;
            }
            if (buf.Length > 0) list.Add(buf);
            return list;
        }

        static void HandleIf(List<string> tokens, ref int ip)
        {
            int flag = Convert.ToInt32(Pop()); if (flag != 0) return;
            int depth = 1;
            for (int i = ip + 1; i < tokens.Count; i++)
            {
                var t = tokens[i].ToUpperInvariant();
                if (t == "IF") depth++;
                else if (t == "ELSE" && depth == 1) { ip = i; return; }
                else if (t == "THEN") { depth--; if (depth == 0) { ip = i; return; } }
            }
            throw new InvalidOperationException("IF without matching THEN");
        }

        static void SkipTo(List<string> tokens, ref int ip, string target)
        {
            int depth = 1;
            for (int i = ip + 1; i < tokens.Count; i++)
            {
                var t = tokens[i].ToUpperInvariant();
                if (t == target && depth == 1) { ip = i; return; }
                else if (t == "IF") depth++; else if (t == "THEN") depth--;
            }
            throw new InvalidOperationException($"No matching {target}");
        }

        static int FindMatching(List<string> tokens, int ip, string startWord, params string[] endWords)
        {
            int depth = 1;
            for (int i = ip + 1; i < tokens.Count; i++)
            {
                var t = tokens[i].ToUpperInvariant();
                if (t == startWord) depth++;
                else if (endWords.Contains(t)) { depth--; if (depth == 0) return i; }
            }
            throw new InvalidOperationException($"No matching end for {startWord}");
        }

        static object Pop() => dataStack.Pop();
        static object Peek() => dataStack.Peek();

        static void InitializeWords()
        {
            // Customized output to format DateTime
            Action<object> writeObj = obj =>
            {
                if (obj is DateTime d)
                {
                    if (d.TimeOfDay == TimeSpan.Zero)
                        Console.WriteLine(d.ToString("yyyy-MM-dd"));
                    else if (d.Second == 0)
                        Console.WriteLine(d.ToString("yyyy-MM-dd' 'HH:mm"));
                    else
                        Console.WriteLine(d.ToString("yyyy-MM-dd' 'HH:mm:ss"));
                }
                else Console.WriteLine(obj);
            };

            // Stack ops
            words["."] = () => writeObj(Pop());
            words[".S"] = () =>
            {
                var arr = dataStack.Reverse().ToArray();
                Console.Write("<" + arr.Length + "> ");
                Console.WriteLine(string.Join(" ", arr.Select(o => o is DateTime dt ?
                    (dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy-MM-dd") :
                     dt.Second == 0 ? dt.ToString("yyyy-MM-dd'T'HH:mm") : dt.ToString("yyyy-MM-dd'T'HH:mm:ss"))
                    : o.ToString())));
            };
            words["swap"] = () => { var b = Pop(); var a = Pop(); dataStack.Push(b); dataStack.Push(a); };
            words["dup"] = () => dataStack.Push(Peek());
            words["-dup"] = () => { var a = Peek(); if (!IsZero(a)) dataStack.Push(a); };
            words["?dup"] = words["-dup"];
            words["over"] = () => { var arr = dataStack.ToArray(); dataStack.Push(arr[1]); };
            words["rot"] = () => { var c = Pop(); var b = Pop(); var a = Pop(); dataStack.Push(b); dataStack.Push(c); dataStack.Push(a); };
            words["drop"] = () => Pop();
            words["pick"] = () => { var n = Convert.ToInt32(Pop()); var arr = dataStack.ToArray(); dataStack.Push(arr[n]); };
            words["roll"] = () => { var n = Convert.ToInt32(Pop()); var lst = dataStack.ToList(); int idx = lst.Count - 1 - n; var v = lst[idx]; lst.RemoveAt(idx); lst.Add(v); dataStack = new Stack<object>(Enumerable.Reverse(lst)); };

            // Constants & variables
            words["constant"] = () => { var name = Pop().ToString(); var val = Pop(); definitions[name] = new List<string> { Convert.ToString(val, CultureInfo.InvariantCulture) }; };
            words["variable"] = () => { var name = Pop().ToString(); definitions[name] = new List<string> { "0" }; };
            words["forget"] = () => { var name = Pop().ToString(); definitions.Remove(name); };

            // Return stack ops
            words[">r"] = () => returnStack.Push(Pop());
            words["r>"] = () => dataStack.Push(returnStack.Pop());

            // Input/output
            words["input"] = () => dataStack.Push(Console.ReadLine());
            words["get"] = () => dataStack.Push(Console.ReadKey(true).KeyChar.ToString());

            // Arithmetic & logic (including date-aware + and -)
            words["+"] = () =>
            {
                var b = Pop(); var a = Pop();
                if (a is DateTime da && (b is double || b is int)) dataStack.Push(da.AddDays(Convert.ToDouble(b)));
                else if (a is double && b is DateTime db) dataStack.Push(db.AddDays(Convert.ToDouble(a)));
                else dataStack.Push(Convert.ToDouble(a) + Convert.ToDouble(b));
            };
            words["-"] = () =>
            {
                var b = Pop(); var a = Pop();
                if (a is DateTime da && b is DateTime db) dataStack.Push((da - db).TotalDays);
                else if (a is DateTime da2) dataStack.Push(da2.AddDays(-Convert.ToDouble(b)));
                else dataStack.Push(Convert.ToDouble(a) - Convert.ToDouble(b));
            };
            words["*"] = () => { var b2 = Convert.ToDouble(Pop()); var a2 = Convert.ToDouble(Pop()); dataStack.Push(a2 * b2); };
            words["/"] = () => { var b2 = Convert.ToDouble(Pop()); var a2 = Convert.ToDouble(Pop()); dataStack.Push(a2 / b2); };
            words["mod"] = () => { var b2 = Convert.ToDouble(Pop()); var a2 = Convert.ToDouble(Pop()); dataStack.Push(a2 % b2); };
            words["/mod"] = () => { var b2 = Convert.ToDouble(Pop()); var a2 = Convert.ToDouble(Pop()); var q = Math.Floor(a2 / b2); var r2 = a2 - q * b2; dataStack.Push(r2); dataStack.Push(q); };
            words["pow"] = () => { var exp = Convert.ToDouble(Pop()); var bas = Convert.ToDouble(Pop()); dataStack.Push(Math.Pow(bas, exp)); };
            words["**"] = words["pow"];
            words["minus"] = () => dataStack.Push(-Convert.ToDouble(Pop()));
            words["="] = () => { var b3 = Convert.ToDouble(Pop()); var a3 = Convert.ToDouble(Pop()); dataStack.Push(a3 == b3 ? 1 : 0); };
            words["<"] = () => { var b3 = Convert.ToDouble(Pop()); var a3 = Convert.ToDouble(Pop()); dataStack.Push(a3 < b3 ? 1 : 0); };
            words[">"] = () => { var b3 = Convert.ToDouble(Pop()); var a3 = Convert.ToDouble(Pop()); dataStack.Push(a3 > b3 ? 1 : 0); };
            words["and"] = () => { var b4 = Convert.ToInt32(Pop()); var a4 = Convert.ToInt32(Pop()); dataStack.Push(a4 & b4); };
            words["or"] = () => { var b4 = Convert.ToInt32(Pop()); var a4 = Convert.ToInt32(Pop()); dataStack.Push(a4 | b4); };
            words["not"] = () => dataStack.Push(Convert.ToInt32(Pop()) == 0 ? 1 : 0);

            // Math functions
            var mathFuncs = new Dictionary<string, Func<double, double>>
            {
                ["sin"] = Math.Sin, ["cos"] = Math.Cos, ["tan"] = Math.Tan,
                ["sinh"] = Math.Sinh, ["cosh"] = Math.Cosh, ["tanh"] = Math.Tanh,
                ["asin"] = Math.Asin, ["acos"] = Math.Acos, ["atan"] = Math.Atan,
                ["asinh"] = Math.Asinh, ["acosh"] = Math.Acosh, ["atanh"] = Math.Atanh,
                ["exp"] = Math.Exp, ["log"] = Math.Log, ["log10"] = Math.Log10,
                ["sqrt"] = Math.Sqrt, ["cbrt"] = Math.Cbrt
            };
            foreach (var kv in mathFuncs)
                words[kv.Key] = () => dataStack.Push(kv.Value(Convert.ToDouble(Pop())));
            words["log2"] = () => dataStack.Push(Math.Log(Convert.ToDouble(Pop()), 2));
            words["abs"] = () => dataStack.Push(Math.Abs(Convert.ToDouble(Pop())));
            words["sign"] = () => dataStack.Push(Math.Sign(Convert.ToDouble(Pop())));
            words["floor"] = () => dataStack.Push(Math.Floor(Convert.ToDouble(Pop())));
            words["ceiling"] = () => dataStack.Push(Math.Ceiling(Convert.ToDouble(Pop())));
            words["round"] = () => dataStack.Push(Math.Round(Convert.ToDouble(Pop())));
            words["truncate"] = () => dataStack.Push(Math.Truncate(Convert.ToDouble(Pop())));
            words["random"] = () => dataStack.Push(new Random().NextDouble());
            words["e"] = () => dataStack.Push(Math.E);
            words["pi"] = () => dataStack.Push(Math.PI);
        }

        static bool IsZero(object v) => Convert.ToDouble(v) == 0;
    }

    class LoopContext { public int Index; public int Limit; public int BeginIp; }
}
