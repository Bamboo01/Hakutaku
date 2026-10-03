using Hakutaku;

namespace simulator
{
    // Prints each check as it happens and keeps the tally. Plain words instead of check
    // marks, so it reads the same in a Windows console and in a CI log.
    class Report
    {
        // On the public domain Caddy answers 404 for every route that isn't public, so a 404
        // there means denied. Locally it doesn't: the app answers 404 for a route that doesn't
        // exist too, and a renamed route would quietly pass as "denied". So 404 only counts
        // when the server isn't this machine, which is taken to mean it is behind Caddy.
        readonly bool notFoundIsDenied;

        public Report(string serverUrl) => notFoundIsDenied = !new Uri(serverUrl).IsLoopback;

        public int Passed { get; private set; }
        public int Failed { get; private set; }

        public void Check(string who, string what, Expect expect, HakutakuError? error, string? detail = null)
        {
            var outcome = error is null ? "OK" + (detail is null ? "" : $"  {detail}") : Describe(error);
            Record(Matches(expect, error), who, what, outcome, $"expected {Describe(expect)}");
        }

        public void Assert(string who, string what, bool ok, string? detail) =>
            Record(ok, who, what, detail ?? "", "");

        public void PrintSummary()
        {
            Console.WriteLine();
            Write(Failed == 0 ? ConsoleColor.Green : ConsoleColor.Red, $"{Passed} as expected, {Failed} unexpected");
            Console.WriteLine();
        }

        public static void Heading(string text)
        {
            Console.WriteLine();
            Line(ConsoleColor.Cyan, text);
        }

        public static void Note(string text) => Line(ConsoleColor.DarkGray, "        " + text);

        public static void Problem(string text) => Line(ConsoleColor.Red, text);

        public static void Line(ConsoleColor color, string text)
        {
            Write(color, text);
            Console.WriteLine();
        }

        public static string Describe(HakutakuError error) =>
            error.HttpCode == 0 ? $"{error.Error}: {error.ErrorMessage}" : $"{error.HttpCode} {error.ErrorMessage}";

        string Describe(Expect expect) => expect switch
        {
            Expect.Ok => "OK",
            Expect.BadRequest => "400",
            Expect.Unauthorized => "401",
            Expect.Forbidden => "403",
            Expect.Conflict => "409",
            Expect.Denied => notFoundIsDenied ? "denied (401, 403 or 404)" : "denied (401 or 403)",
            _ => expect.ToString(),
        };

        bool Matches(Expect expect, HakutakuError? error) => expect switch
        {
            Expect.Ok => error is null,
            Expect.BadRequest => error?.HttpCode == 400,
            Expect.Unauthorized => error?.HttpCode == 401,
            Expect.Forbidden => error?.HttpCode == 403,
            Expect.Conflict => error?.HttpCode == 409,
            Expect.Denied => error?.HttpCode is 401 or 403 || (notFoundIsDenied && error?.HttpCode == 404),
            _ => false,
        };

        void Record(bool ok, string who, string what, string outcome, string expected)
        {
            if (ok) Passed++;
            else Failed++;

            Console.Write("  ");
            Write(ok ? ConsoleColor.Green : ConsoleColor.Red, ok ? "ok  " : "FAIL");
            Console.Write($"  {who,-9} {what,-46} {outcome}");
            if (!ok && expected.Length > 0) Write(ConsoleColor.Red, $"  ({expected})");
            Console.WriteLine();
        }

        static void Write(ConsoleColor color, string text)
        {
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ResetColor();
        }
    }
}
