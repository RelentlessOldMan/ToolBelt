// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Grids
{
    /// <summary>
    /// A cell coordinate in a 2-D grid, addressed as (<see cref="Row"/>, <see cref="Col"/>) with the origin
    /// at the top-left: row 0 is the top row and column 0 the left column. This is the shared coordinate
    /// type for every algorithm in the <c>ToolBelt.Grids</c> division.
    /// </summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public int Row { get; }
        public int Col { get; }

        public Cell(int row, int col)
        {
            Row = row;
            Col = col;
        }

        public void Deconstruct(out int row, out int col)
        {
            row = Row;
            col = Col;
        }

        public bool Equals(Cell other) => Row == other.Row && Col == other.Col;
        public override bool Equals(object? obj) => obj is Cell c && Equals(c);
        public static bool operator ==(Cell a, Cell b) => a.Equals(b);
        public static bool operator !=(Cell a, Cell b) => !a.Equals(b);

        public override int GetHashCode()
        {
            unchecked { return Row * 397 ^ Col; }
        }

        // Invariant: sv-SE printed a negative row with U+2212, fa-IR and ar-SA with bidi marks.
        public override string ToString()
            => "(" + Row.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Col.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// How grid cells are considered adjacent. <see cref="Four"/> connects the four orthogonal neighbours
    /// (von Neumann neighbourhood); <see cref="Eight"/> also connects the four diagonals (Moore
    /// neighbourhood).
    /// </summary>
    public enum Connectivity
    {
        Four = 4,
        Eight = 8,
    }
}
