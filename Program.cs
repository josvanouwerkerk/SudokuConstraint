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

    private record Configuration(string Input, int Offset, string Output)
    {
        public override string ToString() => $"Input file: {Input}\nOutput file: {Output}\nOffset to sudoku for each line: {Offset}";
    }

    /// <summary>
    /// Attempts to create the program configuration from the arguments.
    /// Displays an explanation in the console if this creation fails.
    /// </summary>
    private static Configuration? GetConfiguration(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: SudokuConstraint <input_file> [<sudoku_offset>] [<output_file>]");
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

    /// <summary>
    /// Solves a single Sudoku as supplied by inputLine at the given offset in the line.
    /// After solving, increments the supplied count.
    /// </summary>
    private static string Solve(string inputLine, int offset, ref int count)
    {
        if (inputLine.Length < offset + Variables)
            return inputLine;

        var input = inputLine.AsSpan()[offset..];
        var sudoku = new Sudoku(stackalloc uint[Sudoku.Length]);
        sudoku.Clear();

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
        Debug.Assert(sudoku.Valid(input)); // Validate the solution only in Debug mode.

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

    /// <summary>
    /// Finds the most contrained variable and tries all options for this variable.
    /// The options are ordered from least constraining to most constraining.
    /// Recursively calls itself while the Sudoku isn't fully solved yet.
    /// Only returns true if the Sudoku is fully solved.
    /// </summary>
    private static bool BackTrack(Sudoku sudoku)
    {
        var mostConstrained = -1;
        var optionCount = 10;

        for (var variable = 0; variable < Variables; variable++)
        {
            var current = sudoku[variable].OptionCount;
            if (current < 2 || current >= optionCount)
                continue;

            (mostConstrained, optionCount) = (variable, current);
        }

        Debug.Assert(mostConstrained >= 0);
        var options = sudoku[mostConstrained]; // Select the most constrained variable

        var newData = (Span<uint>)stackalloc uint[Sudoku.Length * optionCount];
        var newIndices = (Span<int>)stackalloc int[optionCount];
        var newOptions = (Span<int>)stackalloc int[optionCount];

        var index = 0;

        while (options)
        {
            newIndices[index] = index;

            var newSudoku = new Sudoku(newData[(index * Sudoku.Length)..]);
            sudoku.CopyTo(newSudoku);

            if (Constrain(newSudoku, mostConstrained, options.FirstOption))
            {
                if (newSudoku.Unsolved == 0)
                {
                    newSudoku.CopyTo(sudoku);
                    return true;
                }

                newOptions[index] = newSudoku.OptionCount; // Obtain all available options to sort by least constraining option
            }

            Variable.ResetFirstOption(ref options);
            index++;
        }

        newOptions.Sort(newIndices, (a, b) => -a.CompareTo(b));

        for (var i = 0; i < newIndices.Length; i++)
        {
            index = newIndices[i];

            if (newOptions[i] == 0)
                continue;

            var newSudoku = new Sudoku(newData[(index * Sudoku.Length)..]);

            if (BackTrack(newSudoku))
            {
                newSudoku.CopyTo(sudoku);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Constrains the selected variable using the provided mask.
    /// Recusively calls itself if the selected variable has one option left, affecting other variables.
    /// Only returns true if the constraining doesn't lead to an invalid set of variables.
    /// </summary>
    private static bool Constrain(Sudoku sudoku, int variable, Variable mask)
    {
        var before = sudoku[variable];
        var after = before & mask;

        if (before == after) // No change
            return true;

        var optionCount = after.OptionCount;
        if (optionCount == 0) // Invalid, no options available for this variable
            return false;

        sudoku[variable] = after;

        if (optionCount == 1)
        {
            sudoku.Unsolved--; // With one option left, the variable is solved
            var affectedMask = ~after;

            var column = variable % 9; // Remove option from others in the same column
            for (var affected = column; affected < Variables; affected += 9)
            {
                if (affected != variable && !Constrain(sudoku, affected, affectedMask))
                    return false;
            }

            var row = variable / 9 * 9; // Remove option from others in the same row
            for (var affected = row; affected < row + 9; affected++)
            {
                if (affected != variable && !Constrain(sudoku, affected, affectedMask))
                    return false;
            }

            var group = column / 3 * 3 + variable / 27 * 27; // Remove option from others in the same group
            for (var vertical = group; vertical < group + 27; vertical += 9)
            {
                for (var affected = vertical; affected < vertical + 3; affected++)
                {
                    if (affected != variable && !Constrain(sudoku, affected, affectedMask))
                        return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Syntactic sugar representing the options that are still possible for a variable.
    /// The options are stored in the lowest 9 bits of an uint.
    /// </summary>
    private readonly record struct Variable(uint Options)
    {
        public const uint Mask = (1U << 9) - 1U;

        public int OptionCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BitOperations.PopCount(Options);
        }

        public bool HasSingleOption
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => OptionCount == 1;
        }

        public Variable FirstOption
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new(Options & (0U - Options)); // Bit manipulation that returns the lowest set bit
        }

        public char Character
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Debug.Assert(HasSingleOption);
                return (char)('1' + BitOperations.TrailingZeroCount(Options));
            }
        }

        public override string ToString() => $"{Options:B9}";

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ResetFirstOption(ref Variable variable)
        {
            variable &= new Variable(variable.Options - 1U); // Bit manipulation that clears the lowest set bit
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Parse(char character, out Variable variable)
        {
            var option = character - '1'; // Convert the digits '0' to '9' to numbers -1 to 8
            if (option >= 0 && option < 9)
            {
                variable = new(1U << option); // Single option if '1' to '9'
                return true;
            }

            variable = new(Mask); // All options if not '1' to '9'
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Variable operator ~(Variable value) => new(~value.Options);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Variable operator &(Variable left, Variable right) => new(left.Options & right.Options);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Variable operator |(Variable left, Variable right) => new(left.Options | right.Options);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator bool(Variable variable) => variable.Options != 0U;
    }

    /// <summary>
    /// Sudoku represented by 82 uints. The first 81 are the options for each of the variables.
    /// The final uint stores the number of unsolved variables that have more than one option.
    /// For speed, it is a ref struct placed "over" a Span on the stack.
    /// </summary>
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

        public Variable this[int variable]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Debug.Assert(variable >= 0 && variable < VariableLength);
                return new(Data[variable]);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                Debug.Assert(variable >= 0 && variable < VariableLength);
                Data[variable] = value.Options;
            }
        }

        public int Unsolved
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)Data[VariableLength];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Data[VariableLength] = (uint)value;
        }

        public int OptionCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var options = BitOperations.PopCount(Data[VariableLength - 1]);
                var data64 = MemoryMarshal.Cast<uint, ulong>(Data[..(VariableLength - 1)]);
                Debug.Assert(data64.Length == 40);

                for (var i = 0; i < data64.Length; i++)
                {
                    options += BitOperations.PopCount(data64[i]); // Make use of the 64 bit PopCount
                }

                return options;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            for (var i = 0; i < Data.Length; i++)
            {
                Data[i] = Variable.Mask; // Initialize to all 9 options possible for each variable
            }

            Unsolved = Variables;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Sudoku destination)
        {
            for (var i = 0; i < Data.Length && i < destination.Data.Length; i++)
            {
                destination.Data[i] = Data[i];
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
                var mask = 0U;
                for (var x = 0; x < 9; x++)
                {
                    mask ^= Data[y * 9 + x];
                }

                if (mask != Variable.Mask)
                    return false;
            }

            return true;
        }

        private bool ValidColumns()
        {
            for (var x = 0; x < 9; x++)
            {
                var mask = 0U;
                for (var y = 0; y < 9; y++)
                {
                    mask ^= Data[y * 9 + x];
                }

                if (mask != Variable.Mask)
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

                var mask = 0U;
                for (var entry = 0; entry < 9; entry++)
                {
                    var ex = entry % 3;
                    var ey = entry / 3;

                    mask ^= Data[(gy + ey) * 9 + gx + ex];
                }

                if (mask != Variable.Mask)
                    return false;
            }

            return true;
        }
    }
}
