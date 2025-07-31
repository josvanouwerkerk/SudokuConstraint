using System.Diagnostics;
using System.Numerics;

namespace SudokuConstraint;

internal class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Program <input_file> [<sudoku_offset>] [<output_file>]");
            // TODO: More information on file format
            return;
        }

        var input = args[0];
        var offset = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : 0;
        var output = args.Length > 2 ? args[2] : $"{input[..^4]}_solved.txt";

        //Console.WriteLine("Waiting 10 seconds before starting...");
        //Thread.Sleep(TimeSpan.FromSeconds(10));
        Console.WriteLine($"Started solving Sudoku's as CSP from {input} to {output} with offset {offset}");

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        var lookup = CreateLookup();
        var count = 0;
        File.WriteAllLines(output, File.ReadLines(input).AsParallel().AsOrdered().Select(line => Solve(lookup, line, offset, ref count)));

        stopwatch.Stop();

        var perSecond = (int)(count / stopwatch.Elapsed.TotalSeconds);
        Console.WriteLine($"Solved {count} Sudoku's in {stopwatch.ElapsedMilliseconds}ms, {perSecond} per second");
    }

    private static ReadOnlyMemory<int> CreateLookup()
    {
        // Creates a lookup from each of the 81 variables to the 3 groups of 8 affected variables in the same row, column and group
        var lookup = new int[81 * 24];

        for (var variable = 0; variable < 81; variable++)
        {
            var indexRow = 24 * variable;
            var indexColumn = indexRow + 8;
            // TODO: Split to 4 without overlap and 4 with
            var indexGroup = indexColumn + 8;

            for (var affectedVariable = 0; affectedVariable < 81; affectedVariable++)
            {
                if (affectedVariable == variable)
                    continue;

                if (affectedVariable % 9 == variable % 9)
                {
                    lookup[indexRow++] = affectedVariable;
                }

                if (affectedVariable / 9 == variable / 9)
                {
                    lookup[indexColumn++] = affectedVariable;
                }

                if ((affectedVariable % 9 / 3 == variable % 9 / 3) && (affectedVariable / 9 / 3 == variable / 9 / 3))
                {
                    lookup[indexGroup++] = affectedVariable;
                }
            }
        }

        return lookup.AsMemory();
    }

    private static string Solve(ReadOnlyMemory<int> lookup, string input, int offset, ref int count)
    {
        if (input.Length < offset + 81)
            return input;

        var sudokuInput = input.AsSpan()[offset..];
        var sudoku = (Span<uint>)stackalloc uint[81]; // Possible value options
        sudoku.Fill((1U << 9) - 1U); // Initialize to all 9 values possible for each variable

        var unsolved = 81; // Keep track of number of unsolved
        for (var variable = 0; variable < 81; variable++)
        {
            var current = sudokuInput[variable] - '1'; // Convert the digits '0' to '9' to numbers -1 to 8
            current = current < -1 || current >= 9 ? -1 : current;

            if (current >= 0)
            {
                unsolved -= Constrain(lookup, sudoku, variable, 1U << current);
            }
        }

#if DEBUG
        if (unsolved > 0)
        {
            Debug.Assert(BackTrack(lookup, sudoku, unsolved));
        }
        Validate(sudokuInput, sudoku);
#else
        if (unsolved > 0)
        {
            Solve(lookup, sudoku, unsolved)
        }
#endif

        var output = (Span<char>)stackalloc char[input.Length];
        input.CopyTo(output);
        var sudokuOutput = output[offset..];

        for (var variable = 0; variable < 81; variable++)
        {
            sudokuOutput[variable] = (char)('1' + BitOperations.TrailingZeroCount(sudoku[variable]));

            // TODO: Can be removed when all are solved
            if (BitOperations.PopCount(sudoku[variable]) != 1)
            {
                sudokuOutput[variable] = '0';
            }
        }

        Interlocked.Increment(ref count);

        return new string(output);
    }

    private static bool BackTrack(ReadOnlyMemory<int> lookup, Span<uint> sudoku, int unsolved)
    {
        var mostConstrained = -1;
        var options = 10;

        for (var variable = 0; variable < sudoku.Length; variable++)
        {
            var current = BitOperations.PopCount(sudoku[variable]);
            if (current < 2 || current >= options)
                continue;

            (mostConstrained, options) = (variable, current);
        }

        var value = sudoku[mostConstrained];

        var newSudokus = (Span<uint>)stackalloc uint[81 * options];
        var information = (Span<Information>)stackalloc Information[options];

        var valueIndex = 0;

        while (value != 0UL)
        {
            information[valueIndex].Index = valueIndex;
            information[valueIndex].Unsolved = unsolved;

            var currentSudoku = newSudokus[(valueIndex * 81)..][..81];
            sudoku.CopyTo(currentSudoku);

            var childCount = Constrain(lookup, currentSudoku, mostConstrained, value & (0U - value));
            if (childCount >= 0)
            {
                if (information[valueIndex].Unsolved == childCount)
                {
                    currentSudoku.CopyTo(sudoku);
                    return true;
                }

                information[valueIndex].Unsolved -= childCount;
                information[valueIndex].Options = GetOptions(currentSudoku);
            }

            value &= value - 1U;
            valueIndex++;
        }

        information.Sort();

        for (var i = 0;i < information.Length;i++)
        {
            if (information[i].Options == 0)
                continue;

            var currentSudoku = newSudokus[(information[i].Index * 81)..][..81];

            if (!BackTrack(lookup, currentSudoku, information[i].Unsolved))
                continue;

            currentSudoku.CopyTo(sudoku);
            return true;
        }

        return false;
    }

    private static int GetOptions(Span<uint> sudoku)
    {
        var options = 0;
        for (var i = 0; i < sudoku.Length; i++)
        {
            options += BitOperations.PopCount(sudoku[i]);
        }
        return options;
    }

    private struct Information : IComparable<Information>
    {
        public int Index;
        public int Options;
        public int Unsolved;

        public readonly int CompareTo(Information other) => other.Options.CompareTo(Options);
    }

    private static int Constrain(ReadOnlyMemory<int> lookup, Span<uint> sudoku, int variable, uint values)
    {
        var before = sudoku[variable];
        var after = before & values;

        if (before == after) // No change
            return 0;

        var valueCount = BitOperations.PopCount(after);
        if (valueCount == 0) // Invalid, no values available anymore
            return -1;

        sudoku[variable] = after;

        var variableLookup = lookup.Span[(24 * variable)..];
        var count = 0;

        // TODO: Inlined function(s)
        if (valueCount == 1)
        {
            count++;

            // TODO: Faster without lookup?

            // TODO: Only first 20 have no overlap
            for (var affectedIndex = 0; affectedIndex < 24; affectedIndex++)
            {
                var affectedCount = Constrain(lookup, sudoku, variableLookup[affectedIndex], ~after);
                if (affectedCount < 0)
                    return -1;

                count += affectedCount;
            }
        }

        for (var group = 0; group < 3; group++)
        {
            var groupLookup = variableLookup[(8 * group)..];

            var oneSet = after;
            var twoSet = 0U;

            for (var affectedIndex = 0; affectedIndex < 8; affectedIndex++)
            {
                var value = sudoku[groupLookup[affectedIndex]];
                if (BitOperations.IsPow2(value))
                {
                    twoSet |= value;
                }
                else
                {
                    twoSet |= oneSet & value;
                    oneSet |= value;
                }
            }

            var onlyOneSet = oneSet & ~twoSet;

            while (onlyOneSet != 0U)
            {
                var value = onlyOneSet & (0U - onlyOneSet);
                onlyOneSet &= onlyOneSet - 1U;

                for (var affectedIndex = 0; affectedIndex < 8; affectedIndex++)
                {
                    var affectedVariable = groupLookup[affectedIndex];
                    if ((sudoku[affectedVariable] & value) != value)
                        continue;

                    var affectedCount = Constrain(lookup, sudoku, affectedVariable, value);
                    if (affectedCount < 0)
                        return -1;

                    count += affectedCount;
                    break;
                }
            }

            var set = after;

            for (var affectedIndex = 0; affectedIndex < 8; affectedIndex++)
            {
                set |= sudoku[groupLookup[affectedIndex]];
            }

            if (BitOperations.PopCount(set) < 9)
                return -1;
        }

        return count;
    }

#if DEBUG
    private static void Validate(ReadOnlySpan<char> input, Span<uint> solved)
    {
        // Matching filled in values
        for (var variable = 0; variable < 81; variable++)
        {
            var current = input[variable] - '1';
            if (current < 0 || current >= 9)
                continue;

            Debug.Assert(solved[variable] == 1U << current);
        }

        // No duplicates in rows
        for (var y = 0; y < 9; y++)
        {
            var count = 0U;
            for (var x = 0; x < 9; x++)
            {
                count ^= solved[y * 9 + x];
            }
            Debug.Assert(count == (1U << 9) - 1U);
        }

        // No duplicates in columns
        for (var x = 0; x < 9; x++)
        {
            var count = 0U;
            for (var y = 0; y < 9; y++)
            {
                count ^= solved[y * 9 + x];
            }
            Debug.Assert(count == (1U << 9) - 1U);
        }

        // No duplicates in groups
        for (var group = 0; group < 9; group++)
        {
            var gx = 3 * (group % 3);
            var gy = 3 * (group / 3);

            var count = 0U;
            for (var entry = 0; entry < 9; entry++)
            {
                var ex = entry % 3;
                var ey = entry / 3;

                count ^= solved[(gy + ey) * 9 + gx + ex];
            }
            Debug.Assert(count == (1U << 9) - 1U);
        }
    }
#endif
}
