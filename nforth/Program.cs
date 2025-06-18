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
                if (token.Equals(":", StringComparison.OrdinalIgnoreCase))
                {
                    ip++;
                    var name = tokens[ip];
                    var body = new List<string>();
                    ip++;
                    while (ip < tokens.Count && tokens[ip] != ";")
                    {
                        body.Add(tokens[ip]);
                        ip++;
                    }
                    definitions[name] = body;
                    continue;
                }
                if (definitions.ContainsKey(token))
                {
                    Evaluate(string.Join(" ", definitions[token]));
                    continue;
                }
                switch (token.ToUpperInvariant())
                {
                    case "IF": HandleIf(tokens, ref ip); continue;
                    case "ELSE": SkipTo(tokens, ref ip, "THEN"); continue;
                    case "THEN": continue;
                    case "DO":
                        {
                            int limit = Convert.ToInt32(Pop());
                            int start = Convert.ToInt32(Pop());
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
                    case "I": dataStack.Push(loopStack.Peek().Index); continue;
                    case "J": dataStack.Push(loopStack.ToArray()[1].Index); continue;
                    case "LEAVE":
                        {
                            loopStack.Pop();
                            ip = FindMatching(tokens, ip, "DO", "LOOP", "+LOOP");
                            continue;
                        }
                    case "BEGIN": beginStack.Push(ip); continue;
                    case "UNTIL":
                        {
                            int flag = Convert.ToInt32(Pop());
                            int startIp = beginStack.Peek();
                            if (flag == 0) ip = startIp; else beginStack.Pop();
                            continue;
                        }
                    case "WHILE":
                        {
                            int flag = Convert.ToInt32(Pop());
                            if (flag == 0) ip = FindMatching(tokens, ip, "WHILE", "REPEAT");
                            continue;
                        }
                    case "REPEAT": ip = beginStack.Pop(); continue;
                    case "STOP": Environment.Exit(0); continue;
                    case "END": continue;
                }
                if (token.StartsWith("\"") && token.EndsWith("\"") && token.Length >= 2)
                {
                    dataStack.Push(token.Substring(1, token.Length - 2));
                    continue;
                }
                var dtFormats = new[] { "yyyy-MM-dd", "yyyy/M/d", "yyyy-M-d", "yyyy/MM/dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" };
                if (DateTime.TryParseExact(token, dtFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    dataStack.Push(dt);
                    continue;
                }
                if (token.StartsWith("T") && TimeSpan.TryParseExact(token.Substring(1), new[] { @"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out var ts))
                {
                    dataStack.Push(ts);
                    continue;
                }
                if (double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out double num))
                {
                    dataStack.Push(num);
                    continue;
                }
                if (words.TryGetValue(token, out var action)) { action(); continue; }
                throw new InvalidOperationException($"Unknown token: {token}");
            }
        }
        static List<string> Tokenize(string input)
        {
            var list = new List<string>(); bool inQ = false; var buf = string.Empty;
            foreach (var ch in input)
            {
                if (ch == '"') { inQ = !inQ; buf += ch; if (!inQ) { list.Add(buf); buf = string.Empty; } continue; }
                if (!inQ && char.IsWhiteSpace(ch)) { if (buf != string.Empty) { list.Add(buf); buf = string.Empty; } } else buf += ch;
            }
            if (buf != string.Empty) list.Add(buf);
            return list;
        }
        static void HandleIf(List<string> tokens, ref int ip)
        {
            if (Convert.ToInt32(Pop()) != 0) return; int depth = 1;
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
                var t = tokens[i].ToUpperInvariant(); if (t == target && depth == 1) { ip = i; return; } else if (t == "IF") depth++; else if (t == "THEN") depth--; }
            throw new InvalidOperationException($"No matching {target}");
        }
        static int FindMatching(List<string> tokens, int ip, string start, params string[] ends)
        {
            int depth = 1;
            for (int i = ip + 1; i < tokens.Count; i++)
            {
                var t = tokens[i].ToUpperInvariant(); if (t == start) depth++; else if (ends.Contains(t)) { depth--; if (depth == 0) return i; }
            }
            throw new InvalidOperationException($"No matching end for {start}");
        }
        static object Pop() => dataStack.Pop();
        static object Peek() => dataStack.Peek();
        static void InitializeWords()
        {
            Action<object> writeObj = obj =>
            {
                if (obj is DateTime d) { Console.WriteLine(d.ToString("yyyy-MM-dd HH:mm:ss")); }
                else if (obj is DateDiff dd)
                {
                    var sign = dd.Negative ? "-" : string.Empty;
                    Console.WriteLine(string.Format("{0}{1:0000}-{2:00}-{3:00} {4:00}:{5:00}:{6:00}", sign, dd.Years, dd.Months, dd.Days, dd.Hours, dd.Minutes, dd.Seconds));
                }
                else if (obj is TimeSpan ts) { Console.WriteLine(ts.ToString()); }
                else { Console.WriteLine(obj); }
            };
            words["."] = () => writeObj(Pop());
            words[".S"] = () =>
            {
                var arr = dataStack.Reverse().ToArray();
                Console.Write("<" + arr.Length + "> ");
                Console.WriteLine(string.Join(" ", arr.Select(o =>
                    o is DateTime dt ? dt.ToString("yyyy-MM-dd HH:mm:ss")
                    : o is DateDiff dd ? string.Format("{0}{1:0000}-{2:00}-{3:00} {4:00}:{5:00}:{6:00}", dd.Negative?"-":"", dd.Years, dd.Months, dd.Days, dd.Hours, dd.Minutes, dd.Seconds)
                    : o.ToString())));
            };
            // ... other word definitions unchanged ...
            // now: push current DateTime
            words["now"] = () => dataStack.Push(DateTime.Now);
            // Arithmetic & logic including DateTime diff
            // Arithmetic & logic including DateTime diff
            words["+"] = () =>
            {
                var o2 = Pop(); var o1 = Pop();
                if (o1 is DateTime d1 && o2 is TimeSpan t2)
                {
                    dataStack.Push(d1.Add(t2));
                }
                else if (o1 is DateTime d1b && o2 is DateDiff dd)
                {
                    // Add DateDiff to DateTime
                    int sign = dd.Negative ? -1 : 1;
                    DateTime dt = d1b;
                    dt = dt.AddYears(sign * dd.Years)
                           .AddMonths(sign * dd.Months)
                           .AddDays(sign * dd.Days)
                           .AddHours(sign * dd.Hours)
                           .AddMinutes(sign * dd.Minutes)
                           .AddSeconds(sign * dd.Seconds);
                    dataStack.Push(dt);
                }
                else
                {
                    dataStack.Push(Convert.ToDouble(o1) + Convert.ToDouble(o2));
                }
            };
            words["-"] = () =>
            {
                var o2 = Pop(); var o1 = Pop();
                if (o1 is DateTime d1 && o2 is DateTime d2)
                {
                    bool neg = d1 < d2;
                    var from = neg ? d1 : d2;
                    var to = neg ? d2 : d1;
                    int Y = to.Year - from.Year;
                    int M = to.Month - from.Month;
                    int D = to.Day - from.Day;
                    int h = to.Hour - from.Hour;
                    int m = to.Minute - from.Minute;
                    int s = to.Second - from.Second;
                    if (s < 0) { s += 60; m--; }
                    if (m < 0) { m += 60; h--; }
                    if (h < 0) { h += 24; D--; }
                    if (D < 0)
                    {
                        var prev = new DateTime(to.Year, to.Month, 1).AddDays(-1);
                        D += DateTime.DaysInMonth(prev.Year, prev.Month);
                        M--;
                    }
                    if (M < 0) { M += 12; Y--; }
                    dataStack.Push(new DateDiff(Y, M, D, h, m, s, neg));
                }
                else dataStack.Push(Convert.ToDouble(o1) - Convert.ToDouble(o2));
            };
        }
    }

    class LoopContext { public int Index; public int Limit; public int BeginIp; }
    class DateDiff { public int Years, Months, Days, Hours, Minutes, Seconds; public bool Negative; public DateDiff(int y,int mo,int d,int h,int mi,int s,bool n){Years=y;Months=mo;Days=d;Hours=h;Minutes=mi;Seconds=s;Negative=n;} }
}
