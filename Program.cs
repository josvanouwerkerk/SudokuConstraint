using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SudokuConstraint;

internal class Program
{
    private const int Variables = 9 * 9;
    private const int VariableData = Variables;
    private const int SudokuData = VariableData + 1;

    public static void Main(string[] args)
    {
        if (GetConfiguration(args) is not { } configuration)
            return;

        Console.WriteLine($"Started solving Sudoku's as Constraint Satisfaction Problem\n{configuration}");

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        var lookup = CreateLookup();
        var count = 0;
        File.WriteAllLines(configuration.Output, File.ReadLines(configuration.Input).AsParallel().AsOrdered().Select(line => Solve(lookup, line, configuration.Offset, ref count)));

        stopwatch.Stop();

        var perSecond = (int)(count / stopwatch.Elapsed.TotalSeconds);
        Console.WriteLine($"Solved {count} Sudoku's in {stopwatch.ElapsedMilliseconds}ms, {perSecond} per second");
    }

    private static Configuration? GetConfiguration(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: Program <input_file> [<sudoku_offset>] [<output_file>]");
            Console.WriteLine();
            Console.WriteLine("Solves Sudoku's as Constraint Satisfaction Problem");
            Console.WriteLine("Input file should contain one Sudoku on each line");
            Console.WriteLine("Input Sudoku should consist of 81 characters, with the characters '1' to '9' for filled in values");
            Console.WriteLine("Offset defaults to 0 and denotes where the Sudoku start on each line");
            Console.WriteLine("Output file will contain the same lines as the input, but with the Sudoku solved");
            Console.WriteLine("Output file defaults to the input file with a \"_solved\" suffix");
            return null;
        }

        var input = args[0];

        if (!File.Exists(input))
        {
            Console.WriteLine($"Can't find input file {input}");
            return null;
        }

        var offset = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : 0;
        var inputDot = input.LastIndexOf('.');
        var output = args.Length > 2 ? args[2] : $"{input[..inputDot]}_solved{input[inputDot..]}";

        return new(input, offset, output);
    }

    private static ReadOnlyMemory<int> CreateLookup()
    {
        // Creates a lookup from each of the 81 variables to the 3 groups of 8 affected variables in the same row, column and group
        // The last 4 values are overlapped between two groups
        var lookup = new int[Variables * 24];

        for (var variable = 0; variable < Variables; variable++)
        {
            var indexRow = 24 * variable;
            var indexColumn = indexRow + 8;
            var indexGroup = indexColumn + 8;
            var indexGroupOverlap = indexGroup + 4;

            for (var affectedVariable = 0; affectedVariable < Variables; affectedVariable++)
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
                    if ((affectedVariable % 9 == variable % 9) || (affectedVariable / 9 == variable / 9))
                    {
                        lookup[indexGroupOverlap++] = affectedVariable;
                    }
                    else
                    {
                        lookup[indexGroup++] = affectedVariable;
                    }
                }
            }
        }

        return lookup.AsMemory();
    }

    private static string Solve(ReadOnlyMemory<int> lookup, string input, int offset, ref int count)
    {
        if (input.Length < offset + Variables)
            return input;

        var sudokuInput = input.AsSpan()[offset..];
        var sudoku = new Sudoku(stackalloc uint[SudokuData]);
        sudoku.Initialize();

        for (var variable = 0; variable < Variables; variable++)
        {
            var current = sudokuInput[variable] - '1'; // Convert the digits '0' to '9' to numbers -1 to 8
            current = current < -1 || current >= 9 ? -1 : current;

            if (current >= 0)
            {
                Constrain(lookup, sudoku, variable, 1U << current);
            }
        }

        if (sudoku.Unsolved > 0)
        {
            var solved = BackTrack(lookup, sudoku);
            Debug.Assert(solved);
        }
        Debug.Assert(Validate(sudokuInput, sudoku));

        var output = (Span<char>)stackalloc char[input.Length];
        input.CopyTo(output);
        var sudokuOutput = output[offset..];

        for (var variable = 0; variable < Variables; variable++)
        {
            sudokuOutput[variable] = (char)('1' + GetFirstOption(sudoku[variable]));
        }

        Interlocked.Increment(ref count);

        return new string(output);
    }

    private static bool BackTrack(ReadOnlyMemory<int> lookup, Sudoku sudoku)
    {
        var mostConstrained = -1;
        var options = 10;

        for (var variable = 0; variable < Variables; variable++)
        {
            var current = GetOptionCount(sudoku[variable]);
            if (current < 2 || current >= options)
                continue;

            (mostConstrained, options) = (variable, current);
        }

        var value = sudoku[mostConstrained];

        var newData = (Span<uint>)stackalloc uint[SudokuData * options];
        var newIndices = (Span<int>)stackalloc int[options];
        var newOptions = (Span<int>)stackalloc int[options];

        var valueIndex = 0;

        while (value != 0UL)
        {
            newIndices[valueIndex] = valueIndex;

            var currentSudoku = new Sudoku(newData[(valueIndex * SudokuData)..][..SudokuData]);
            sudoku.CopyTo(currentSudoku);

            if (Constrain(lookup, currentSudoku, mostConstrained, GetFirstOptionMask(value)))
            {
                if (currentSudoku.Unsolved == 0)
                {
                    currentSudoku.CopyTo(sudoku);
                    return true;
                }

                newOptions[valueIndex] = currentSudoku.OptionCount;
            }

            value = ResetFirstOption(value);
            valueIndex++;
        }

        newOptions.Sort(newIndices, (a, b) => -a.CompareTo(b));

        for (var i = 0; i < newIndices.Length; i++)
        {
            var index = newIndices[i];

            if (newOptions[i] == 0)
                continue;

            var currentSudoku = new Sudoku(newData[(index * SudokuData)..][..SudokuData]);

            if (!BackTrack(lookup, currentSudoku))
                continue;

            currentSudoku.CopyTo(sudoku);
            return true;
        }

        return false;
    }

    private static bool Constrain(ReadOnlyMemory<int> lookup, Sudoku sudoku, int variable, uint values)
    {
        var before = sudoku[variable];
        var after = before & values;

        if (before == after) // No change
            return true;

        var valueCount = GetOptionCount(after);
        if (valueCount == 0) // Invalid, no values available anymore
            return false;

        sudoku[variable] = after;

        var variableLookup = lookup.Span[(24 * variable)..];

        // TODO: Inlined function(s)
        if (valueCount == 1)
        {
            sudoku.Unsolved--;

            // TODO: Faster without lookup?
            for (var affectedIndex = 0; affectedIndex < 20; affectedIndex++)
            {
                if (!Constrain(lookup, sudoku, variableLookup[affectedIndex], ~after))
                    return false;
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
                if (HasSingleOption(value))
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

                    if (!Constrain(lookup, sudoku, affectedVariable, value))
                        return false;

                    break;
                }
            }

            var set = after;

            for (var affectedIndex = 0; affectedIndex < 8; affectedIndex++)
            {
                set |= sudoku[groupLookup[affectedIndex]];
            }

            if (GetOptionCount(set) < 9)
                return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetOptionCount(uint options) => BitOperations.PopCount(options);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetOptionCount(ulong options) => BitOperations.PopCount(options);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasSingleOption(uint options) => BitOperations.IsPow2(options);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetFirstOption(uint options) => BitOperations.TrailingZeroCount(options);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetFirstOptionMask(uint options) => options & (0U - options);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ResetFirstOption(uint options) => options & (options - 1U);

    private record Configuration(string Input, int Offset, string Output)
    {
        public override string ToString() => $"Input file: {Input}\nOutput file: {Output}\nOffset to sudoku for each line: {Offset}";
    }

    private readonly ref struct Sudoku
    {
        public readonly Span<uint> Data;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sudoku(Span<uint> data)
        {
            Debug.Assert(data.Length == SudokuData);
            Data = data;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Initialize()
        {
            Data[..VariableData].Fill((1U << 9) - 1U); // Initialize to all 9 options possible for each variable
            Unsolved = Variables;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Sudoku destination)
        {
            Data.CopyTo(destination.Data);
        }

        public uint this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Data[index];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Data[index] = value;
        }

        public int Unsolved
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)Data[^1];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Data[^1] = (uint)value;
        }

        public int OptionCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var options = 0;
                var data64 = MemoryMarshal.Cast<uint, ulong>(Data);
                for (var i = 0; i < data64.Length; i++)
                {
                    options += GetOptionCount(data64[i]);
                }
                options += GetOptionCount(Data[^1]);
                return options;
            }
        }
    }

    private static bool Validate(ReadOnlySpan<char> input, Sudoku solved)
    {
        return ValidateInput(input, solved) && ValidateRows(solved) && ValidateColumns(solved) && ValidateGroups(solved);
    }

    private static bool ValidateInput(ReadOnlySpan<char> input, Sudoku solved)
    {
        for (var variable = 0; variable < Variables; variable++)
        {
            var current = input[variable] - '1';
            if (current < 0 || current >= 9)
                continue;

            if (solved[variable] != 1U << current)
                return false;
        }

        return true;
    }

    private static bool ValidateRows(Sudoku solved)
    {
        for (var y = 0; y < 9; y++)
        {
            var count = 0U;
            for (var x = 0; x < 9; x++)
            {
                count ^= solved[y * 9 + x];
            }

            if (count != (1U << 9) - 1U)
                return false;
        }

        return true;
    }

    private static bool ValidateColumns(Sudoku solved)
    {
        for (var x = 0; x < 9; x++)
        {
            var count = 0U;
            for (var y = 0; y < 9; y++)
            {
                count ^= solved[y * 9 + x];
            }

            if (count != (1U << 9) - 1U)
                return false;
        }

        return true;
    }

    private static bool ValidateGroups(Sudoku solved)
    {
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

            if (count != (1U << 9) - 1U)
                return false;
        }

        return true;
    }
}
