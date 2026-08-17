namespace Arronix.Host.Engines.Matching;

/// <summary>
/// The Munkres (Hungarian) optimal-assignment algorithm over a rectangular cost matrix.
/// </summary>
/// <remarks>
/// <para>
/// Ported from Lidarr's <c>Munkres</c>
/// (<c>_reference/Lidarr/src/NzbDrone.Core/MediaFiles/TrackImport/Identification/Munkres.cs</c>, itself
/// MIT-licensed work of Robert A. Pilgrim, Murray State University) — 504 generic lines with zero media
/// nouns, which is the existence proof that the hardest surveyed matching case is data plus a generic
/// solver. The port keeps the step structure and the rectangular padding; only spelling and array shape
/// changed.
/// </para>
/// </remarks>
internal sealed class MunkresSolver
{
    private readonly double[][] _costs;
    private readonly double[][] _working;
    private readonly int[][] _marks;
    private readonly int[][] _path;
    private readonly int[] _rowCover;
    private readonly int[] _columnCover;
    private readonly int _size;
    private readonly int _rows;
    private readonly int _columns;
    private int _pathCount;
    private int _pathRow;
    private int _pathColumn;

    /// <summary>
    /// Initializes a new instance of the <see cref="MunkresSolver"/> class.
    /// </summary>
    /// <param name="costMatrix">The rectangular cost matrix, row per source and column per target.</param>
    internal MunkresSolver(double[][] costMatrix)
    {
        _rows = costMatrix.Length;
        _columns = _rows > 0 ? costMatrix[0].Length : 0;
        _size = Math.Max(_rows, _columns);

        _costs = new double[_size][];
        _working = new double[_size][];
        _marks = new int[_size][];
        for (var row = 0; row < _size; row++)
        {
            _costs[row] = new double[_size];
            _working[row] = new double[_size];
            _marks[row] = new int[_size];

            if (row >= _rows)
            {
                continue;
            }

            for (var column = 0; column < _columns; column++)
            {
                _costs[row][column] = costMatrix[row][column];
                _working[row][column] = costMatrix[row][column];
            }
        }

        _path = new int[(2 * _size) + 1][];
        for (var i = 0; i < _path.Length; i++)
        {
            _path[i] = new int[2];
        }

        _rowCover = new int[_size];
        _columnCover = new int[_size];
    }

    /// <summary>
    /// Runs the algorithm and returns the optimal assignment.
    /// </summary>
    /// <returns>The assigned pairs, one per matched row and column of the original matrix.</returns>
    internal IReadOnlyList<(int Row, int Column)> Solve()
    {
        var step = 1;
        while (step != 7)
        {
            step = step switch
            {
                1 => ReduceRows(),
                2 => StarZeros(),
                3 => CoverStarredColumns(),
                4 => PrimeZeros(),
                5 => AugmentPath(),
                6 => AdjustBySmallestUncovered(),
                _ => 7,
            };
        }

        var solution = new List<(int Row, int Column)>();
        for (var row = 0; row < _rows; row++)
        {
            for (var column = 0; column < _columns; column++)
            {
                if (_marks[row][column] == 1)
                {
                    solution.Add((row, column));
                }
            }
        }

        return solution;
    }

    /// <summary>
    /// Gets the total original cost of one solution.
    /// </summary>
    /// <param name="solution">The assignment to price.</param>
    /// <returns>The summed cost.</returns>
    internal double CostOf(IReadOnlyList<(int Row, int Column)> solution)
    {
        var total = 0.0;
        foreach (var (row, column) in solution)
        {
            total += _costs[row][column];
        }

        return total;
    }

    // Step 1: subtract each row's minimum from the row.
    private int ReduceRows()
    {
        for (var row = 0; row < _size; row++)
        {
            var minimum = _working[row][0];
            for (var column = 1; column < _size; column++)
            {
                minimum = Math.Min(minimum, _working[row][column]);
            }

            for (var column = 0; column < _size; column++)
            {
                _working[row][column] -= minimum;
            }
        }

        return 2;
    }

    // Step 2: star each zero with no starred zero in its row or column.
    private int StarZeros()
    {
        for (var row = 0; row < _size; row++)
        {
            for (var column = 0; column < _size; column++)
            {
                if (_working[row][column] == 0 && _rowCover[row] == 0 && _columnCover[column] == 0)
                {
                    _marks[row][column] = 1;
                    _rowCover[row] = 1;
                    _columnCover[column] = 1;
                }
            }
        }

        ClearCovers();
        return 3;
    }

    // Step 3: cover each column containing a starred zero; done when every column is covered.
    private int CoverStarredColumns()
    {
        for (var row = 0; row < _size; row++)
        {
            for (var column = 0; column < _size; column++)
            {
                if (_marks[row][column] == 1)
                {
                    _columnCover[column] = 1;
                }
            }
        }

        var covered = 0;
        for (var column = 0; column < _size; column++)
        {
            covered += _columnCover[column];
        }

        return covered >= _size ? 7 : 4;
    }

    // Step 4: prime uncovered zeros, shifting covers, until an augmenting path start is found.
    private int PrimeZeros()
    {
        while (true)
        {
            var (row, column) = FindUncoveredZero();
            if (row == -1)
            {
                return 6;
            }

            _marks[row][column] = 2;
            var starredColumn = FindStarInRow(row);
            if (starredColumn == -1)
            {
                _pathRow = row;
                _pathColumn = column;
                return 5;
            }

            _rowCover[row] = 1;
            _columnCover[starredColumn] = 0;
        }
    }

    // Step 5: alternate primed and starred zeros along the path, flipping stars.
    private int AugmentPath()
    {
        _pathCount = 1;
        _path[0][0] = _pathRow;
        _path[0][1] = _pathColumn;

        while (true)
        {
            var row = FindStarInColumn(_path[_pathCount - 1][1]);
            if (row == -1)
            {
                break;
            }

            _path[_pathCount][0] = row;
            _path[_pathCount][1] = _path[_pathCount - 1][1];
            _pathCount++;

            var column = FindPrimeInRow(_path[_pathCount - 1][0]);
            _path[_pathCount][0] = _path[_pathCount - 1][0];
            _path[_pathCount][1] = column;
            _pathCount++;
        }

        for (var i = 0; i < _pathCount; i++)
        {
            _marks[_path[i][0]][_path[i][1]] = _marks[_path[i][0]][_path[i][1]] == 1 ? 0 : 1;
        }

        ClearCovers();
        ErasePrimes();
        return 3;
    }

    // Step 6: add the smallest uncovered value to covered rows, subtract it from uncovered columns.
    private int AdjustBySmallestUncovered()
    {
        var minimum = double.MaxValue;
        for (var row = 0; row < _size; row++)
        {
            for (var column = 0; column < _size; column++)
            {
                if (_rowCover[row] == 0 && _columnCover[column] == 0)
                {
                    minimum = Math.Min(minimum, _working[row][column]);
                }
            }
        }

        for (var row = 0; row < _size; row++)
        {
            for (var column = 0; column < _size; column++)
            {
                if (_rowCover[row] == 1)
                {
                    _working[row][column] += minimum;
                }

                if (_columnCover[column] == 0)
                {
                    _working[row][column] -= minimum;
                }
            }
        }

        return 4;
    }

    private (int Row, int Column) FindUncoveredZero()
    {
        for (var row = 0; row < _size; row++)
        {
            if (_rowCover[row] != 0)
            {
                continue;
            }

            for (var column = 0; column < _size; column++)
            {
                if (_working[row][column] == 0 && _columnCover[column] == 0)
                {
                    return (row, column);
                }
            }
        }

        return (-1, -1);
    }

    private int FindStarInRow(int row)
    {
        for (var column = 0; column < _size; column++)
        {
            if (_marks[row][column] == 1)
            {
                return column;
            }
        }

        return -1;
    }

    private int FindStarInColumn(int column)
    {
        for (var row = 0; row < _size; row++)
        {
            if (_marks[row][column] == 1)
            {
                return row;
            }
        }

        return -1;
    }

    private int FindPrimeInRow(int row)
    {
        for (var column = 0; column < _size; column++)
        {
            if (_marks[row][column] == 2)
            {
                return column;
            }
        }

        return -1;
    }

    private void ClearCovers()
    {
        Array.Clear(_rowCover);
        Array.Clear(_columnCover);
    }

    private void ErasePrimes()
    {
        for (var row = 0; row < _size; row++)
        {
            for (var column = 0; column < _size; column++)
            {
                if (_marks[row][column] == 2)
                {
                    _marks[row][column] = 0;
                }
            }
        }
    }
}
