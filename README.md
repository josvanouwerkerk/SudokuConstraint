This repository contains a C# solver for standard Sudoku puzzles. (Standard means 9 by 9 with regular 3 by 3 groups.)
The solver reads the Sudoku's from a file and writes the solved Sudoku's to another file.

Usage: Program <input_file> [<sudoku_offset>] [<output_file>]

- Solves Sudoku's as Constraint Satisfaction Problem
- Input file should contain one Sudoku on each line
- Input Sudoku should consist of 81 characters, with the characters '1' to '9' for filled in values
- Offset defaults to 0 and denotes where the Sudoku starts on each line
- Output file will contain the same lines as the input, but with the Sudoku solved
- Output file defaults to the input file with a "_solved" suffix

No files with Sudoku puzzles are provided in this repository. A good source can be found here:
https://github.com/grantm/sudoku-exchange-puzzle-bank

For these files the sudoku_offset parameter should be set to 13


For the medium set of puzzles above, this solver solves about 200.000 Sudoku's per second on my machine.
