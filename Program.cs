using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SudokuConstraint;

internal class Program
{
    private const int Variables = 9 * 9;

    public static void Main(string[] args)
    {
        if (GetConfiguration(args) is not { } configuration)
            return;

        Console.WriteLine($"Started solving Sudoku's as Constraint Satisfaction Problem\n{configuration}");

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        var count = 0;
        File.WriteAllLines(configuration.Output, File.ReadLines(configuration.Input).AsParallel().AsOrdered().Select(line => Solve(line, configuration.Offset, ref count)));

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
            Console.WriteLine("Offset defaults to 0 and denotes where the Sudoku starts on each line");
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

    private static string Solve(string inputLine, int offset, ref int count)
    {
        if (inputLine.Length < offset + Variables)
            return inputLine;

        var input = inputLine.AsSpan()[offset..];
        var sudoku = new Sudoku(stackalloc uint[Sudoku.Length]);
        sudoku.Initialize();

        for (var variable = 0; variable < Variables; variable++)
        {
            if (Variable.Parse(input[variable], out var value))
            {
                Constrain(sudoku, variable, value);
            }
        }

        if (sudoku.Unsolved > 0)
        {
            var solved = BackTrack(sudoku);
            Debug.Assert(solved);
        }
        Debug.Assert(sudoku.Valid(input));

        var outputLine = (Span<char>)stackalloc char[inputLine.Length];
        inputLine.CopyTo(outputLine);
        var output = outputLine[offset..];

        for (var variable = 0; variable < Variables; variable++)
        {
            output[variable] = sudoku[variable].Character;
        }

        Interlocked.Increment(ref count);

        return new string(outputLine);
    }

    private static bool BackTrack(Sudoku sudoku)
    {
        var mostConstrained = -1;
        var options = 10;

        for (var variable = 0; variable < Variables; variable++)
        {
            var current = sudoku[variable].OptionCount;
            if (current < 2 || current >= options)
                continue;

            (mostConstrained, options) = (variable, current);
        }

        Debug.Assert(mostConstrained >= 0);
        var value = sudoku[mostConstrained];

        var newData = (Span<uint>)stackalloc uint[Sudoku.Length * options];
        var newIndices = (Span<int>)stackalloc int[options];
        var newOptions = (Span<int>)stackalloc int[options];

        var valueIndex = 0;

        while (value)
        {
            newIndices[valueIndex] = valueIndex;

            var currentSudoku = new Sudoku(newData[(valueIndex * Sudoku.Length)..]);
            sudoku.CopyTo(currentSudoku);

            if (Constrain(currentSudoku, mostConstrained, value.FirstOptionMask))
            {
                if (currentSudoku.Unsolved == 0)
                {
                    currentSudoku.CopyTo(sudoku);
                    return true;
                }

                newOptions[valueIndex] = currentSudoku.OptionCount;
            }

            Variable.ResetFirstOption(ref value);
            valueIndex++;
        }

        newOptions.Sort(newIndices, (a, b) => -a.CompareTo(b));

        for (var i = 0; i < newIndices.Length; i++)
        {
            var index = newIndices[i];

            if (newOptions[i] == 0)
                continue;

            var currentSudoku = new Sudoku(newData[(index * Sudoku.Length)..]);

            if (!BackTrack(currentSudoku))
                continue;

            currentSudoku.CopyTo(sudoku);
            return true;
        }

        return false;
    }

    private static bool Constrain(Sudoku sudoku, int variable, Variable mask)
    {
        var before = sudoku[variable];
        var after = before & mask;

        if (before == after) // No change
            return true;

        var valueCount = after.OptionCount;
        if (valueCount == 0) // Invalid, no values available anymore
            return false;

        sudoku[variable] = after;

        if (valueCount == 1)
        {
            sudoku.Unsolved--;

            var columnStart = variable % 9;
            for (var column = columnStart; column < Variables; column += 9)
            {
                if (column != variable && !Constrain(sudoku, column, ~after))
                    return false;
            }

            var rowStart = 9 * (variable / 9);
            for (var row = rowStart; row < rowStart + 9; row++)
            {
                if (row != variable && !Constrain(sudoku, row, ~after))
                    return false;
            }

            var groupStart = (variable % 9 / 3 * 3) + variable / 27 * 27;
            for (var groupY = 0; groupY < 27; groupY += 9)
            {
                for (var groupX = 0; groupX < 3; groupX++)
                {
                    var group = groupStart + groupX + groupY;
                    if (group != variable && !Constrain(sudoku, group, ~after))
                        return false;
                }
            }
        }

        return true;
    }

    private record Configuration(string Input, int Offset, string Output)
    {
        public override string ToString() => $"Input file: {Input}\nOutput file: {Output}\nOffset to sudoku for each line: {Offset}";
    }

    private readonly record struct Variable(uint Value)
    {
        public const uint Mask = (1U << 9) - 1U;

        public int OptionCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BitOperations.PopCount(Value);
        }

        public bool HasSingleOption
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BitOperations.IsPow2(Value);
        }

        public Variable FirstOptionMask
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new(Value & (0U - Value));
        }

        public char Character
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Debug.Assert(HasSingleOption);
                return (char)('1' + BitOperations.TrailingZeroCount(Value));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ResetFirstOption(ref Variable variable) => variable &= new Variable(variable.Value - 1U);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Parse(char character, out Variable variable)
        {
            var option = character - '1'; // Convert the digits '0' to '9' to numbers -1 to 8
            if (option >= 0 && option < 9)
            {
                variable = new(1U << option);
                return true;
            }

            variable = new(Mask);
            return false;
        }

        public override string ToString() => $"{Value:B9}";

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Variable operator ~(Variable value) => new(~value.Value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Variable operator &(Variable left, Variable right) => new(left.Value & right.Value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Variable operator |(Variable left, Variable right) => new(left.Value | right.Value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator bool(Variable variable) => variable.Value != 0U;
    }

    private readonly record struct Triplet(uint Value)
    {
        public const uint Mask = (1U << 27) - 1U;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Triplet(Variable variable, int index) : this(FromVariable(variable.Value, index)) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint FromVariable(uint variable, int index)
        {
            Debug.Assert(index >= 0 && index < 3);
            var shift = 9 * index;
            return ~(Variable.Mask << shift) | (variable << shift);
        }

        public Variable this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Debug.Assert(index >= 0 && index < 3);
                var shift = 9 * index;
                return new((Value >> shift) & Variable.Mask);
            }
        }

        public int Option1Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BitOperations.PopCount(Value & Variable.Mask);
        }

        public int Option2Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BitOperations.PopCount(Value & (Variable.Mask << 9));
        }

        public int Option3Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BitOperations.PopCount(Value & (Variable.Mask << 18));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Parse(ReadOnlySpan<char> input, out Triplet variable)
        {
            Debug.Assert(input.Length >= 3);
            var result = Variable.Parse(input[0], out var variable1) & Variable.Parse(input[1], out var variable2) & Variable.Parse(input[2], out var variable3);
            variable = new(variable1.Value | (variable2.Value << 9) | (variable3.Value << 18));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(Span<char> output)
        {
            Debug.Assert(output.Length >= 3);
            output[0] = this[0].Character;
            output[1] = this[1].Character;
            output[2] = this[2].Character;
        }

        public override string ToString() => $"{this[0]}/{this[1]}/{this[2]}";

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Triplet operator ~(Triplet value) => new(~value.Value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Triplet operator &(Triplet left, Triplet right) => new(left.Value & right.Value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Triplet operator |(Triplet left, Triplet right) => new(left.Value | right.Value);
    }

    private readonly ref struct Sudoku
    {
        public const int VariableLength = Variables;
        public const int Length = VariableLength + 1;

        public readonly Span<uint> Data;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sudoku(Span<uint> data)
        {
            Debug.Assert(data.Length >= Length);
            Data = data[..Length];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Initialize()
        {
            Data[..VariableLength].Fill(Variable.Mask); // Initialize to all 9 options possible for each variable
            Unsolved = Variables;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Sudoku destination)
        {
            Data.CopyTo(destination.Data);
        }

        public Variable this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Debug.Assert(index >= 0 && index < VariableLength);
                return new(Data[index]);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                Debug.Assert(index >= 0 && index < VariableLength);
                Data[index] = value.Value;
            }
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
                var data64 = MemoryMarshal.Cast<uint, ulong>(Data[..^2]);
                for (var i = 0; i < data64.Length; i++)
                {
                    options += BitOperations.PopCount(data64[i]);
                }
                options += BitOperations.PopCount(Data[^2]);
                return options;
            }
        }

        public bool Valid(ReadOnlySpan<char> input)
        {
            return ValidInput(input) && ValidRows() && ValidColumns() && ValidGroups();
        }

        private bool ValidInput(ReadOnlySpan<char> input)
        {
            for (var variable = 0; variable < Variables; variable++)
            {
                if (Variable.Parse(input[variable], out var value) && this[variable] != value)
                    return false;
            }

            return true;
        }

        private bool ValidRows()
        {
            for (var y = 0; y < 9; y++)
            {
                var count = 0U;
                for (var x = 0; x < 9; x++)
                {
                    count ^= Data[y * 9 + x];
                }

                if (count != Variable.Mask)
                    return false;
            }

            return true;
        }

        private bool ValidColumns()
        {
            for (var x = 0; x < 9; x++)
            {
                var count = 0U;
                for (var y = 0; y < 9; y++)
                {
                    count ^= Data[y * 9 + x];
                }

                if (count != Variable.Mask)
                    return false;
            }

            return true;
        }

        private bool ValidGroups()
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

                    count ^= Data[(gy + ey) * 9 + gx + ex];
                }

                if (count != Variable.Mask)
                    return false;
            }

            return true;
        }
    }
}
