using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WinSbi.Olap.Core;

public static class MayoristasHistoricoReport
{
    public const string CatalogName = "SBI-Ordenes-Pedidos";
    public const string CubeName = "Ordenes-Pedidos";
    public const string ProviderType = "DISTRIBUIDOR MAYORISTA";
    public const int ManifestSchemaVersion = 1;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static MayoristasHistoricoReportOptions CreateOptions(
        int startYear,
        int startMonth,
        int endYear,
        int endMonth,
        string measure,
        string product,
        string buyerScope,
        bool resume)
    {
        ValidateMonth(startMonth, "--start-month");
        ValidateMonth(endMonth, "--end-month");
        if (startYear < 1900)
        {
            throw new ArgumentOutOfRangeException(nameof(startYear), "--start-year must be 1900 or later.");
        }

        if (endYear < 1900)
        {
            throw new ArgumentOutOfRangeException(nameof(endYear), "--end-year must be 1900 or later.");
        }

        if ((endYear, endMonth).CompareTo((startYear, startMonth)) < 0)
        {
            throw new ArgumentException("The historical end period must not precede the start period.");
        }

        var measureSelection = MayoristasReport.ParseMeasure(measure);
        if (measureSelection == MayoristasMeasureSelection.Both)
        {
            throw new ArgumentException("report mayoristas_historico accepts one measure: accepted or dispatched.");
        }

        var parts = Enumerable.Range(startYear, endYear - startYear + 1)
            .Select(year =>
            {
                var firstMonth = year == startYear ? startMonth : 1;
                var lastMonth = year == endYear ? endMonth : 12;
                return new MayoristasHistoricoYearPart(
                    year,
                    Enumerable.Range(firstMonth, lastMonth - firstMonth + 1).ToList());
            })
            .ToList();

        return new MayoristasHistoricoReportOptions(
            startYear,
            startMonth,
            endYear,
            endMonth,
            parts,
            $"{startYear:0000}-{startMonth:00}_{endYear:0000}-{endMonth:00}",
            measureSelection,
            MayoristasReport.ParseProduct(product),
            EdsPercentilesReport.ParseBuyerScope(buyerScope),
            resume);
    }

    public static string DefaultOutputDirectory(MayoristasHistoricoReportOptions options)
    {
        return Path.Combine("reports", CatalogName, "mayoristas_historico", options.PeriodLabel);
    }

    public static string BuildMdx(
        MayoristasHistoricoReportOptions options,
        MayoristasHistoricoYearPart part)
    {
        if (!options.YearParts.Any(candidate => candidate.Year == part.Year && candidate.Months.SequenceEqual(part.Months)))
        {
            throw new ArgumentException("The requested year part does not belong to the historical period.", nameof(part));
        }

        var monthMembers = string.Join($",{Environment.NewLine}", part.Months.Select(static month =>
            $"    [MOVIMIENTOS ORDEN PEDIDO].[MES DESPACHO].&[{month}]"));
        var productMembers = string.Join($",{Environment.NewLine}", ProductUniqueNames(options.Product).Select(static product =>
            $"    [PRODUCTO].[DESCRIPCION PRODUCTO].&[{product}]"));
        var buyerMembers = string.Join($",{Environment.NewLine}", BuyerSubtypeUniqueNames(options.BuyerScope).Select(static buyer =>
            $"    [COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[{buyer}]"));

        return $$"""
WITH
SET [target_months] AS {
{{monthMembers}}
}
SET [target_buyers] AS {
{{buyerMembers}}
}
SET [target_products] AS {
{{productMembers}}
}
SET [rows] AS
    NONEMPTY(
        [PROVEEDOR].[NOMBRE COMERCIAL PROVEEDOR].[NOMBRE COMERCIAL PROVEEDOR].Members
        * [target_months]
        * { [MOVIMIENTOS ORDEN PEDIDO].[AÑO DESPACHO].&[{{part.Year}}] }
        * [target_products]
        * [target_buyers],
        {{MeasureUniqueName(options.Measure)}}
    )
SELECT
    { {{MeasureUniqueName(options.Measure)}} } ON COLUMNS,
    [rows] ON ROWS
FROM [{{CubeName}}]
WHERE ([PROVEEDOR].[TIPO AGENTE PROVEEDOR].&[{{ProviderType}}])
""";
    }

    public static async Task<MayoristasHistoricoReportResult> GenerateAsync(
        IXmlaClient client,
        MayoristasHistoricoReportOptions options,
        string outputDirectory,
        string equivalentCommand,
        XmlaDebugOptions? debugOptions = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var partsDirectory = Path.Combine(outputDirectory, "parts");
        Directory.CreateDirectory(partsDirectory);

        var manifestPath = Path.Combine(outputDirectory, "mayoristas-historico-manifest.json");
        var runTimestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var completedParts = new List<MayoristasHistoricoPartManifest>();

        if (options.Resume && File.Exists(manifestPath))
        {
            var existing = JsonSerializer.Deserialize<MayoristasHistoricoManifest>(
                await File.ReadAllTextAsync(manifestPath, cancellationToken),
                ManifestJsonOptions) ?? throw new InvalidDataException("The historical manifest is empty or invalid.");
            ValidateManifestCompatibility(existing, options);
            runTimestamp = existing.GeneratedAtUtc;
            completedParts.AddRange(existing.Parts);
        }
        else if (options.Resume && Directory.EnumerateFiles(partsDirectory, "*.csv").Any())
        {
            throw new InvalidDataException("Cannot resume historical parts without a compatible manifest.");
        }

        if (!options.Resume)
        {
            completedParts.Clear();
        }

        await WriteManifestAsync(
            manifestPath,
            CreateManifest(options, "in_progress", runTimestamp, equivalentCommand, completedParts, 0, 0),
            cancellationToken);

        var detailTables = new List<MdxTable>();
        var partPaths = new List<string>();
        foreach (var part in options.YearParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partPath = Path.Combine(partsDirectory, $"{part.Year:0000}.csv");
            var relativePartPath = $"parts/{part.Year:0000}.csv";
            var completed = completedParts.FirstOrDefault(candidate => candidate.Year == part.Year);
            MdxTable detailPart;

            if (options.Resume && completed is not null)
            {
                if (!completed.Months.SequenceEqual(part.Months))
                {
                    throw new InvalidDataException($"Manifest months for {part.Year} do not match the requested period.");
                }

                if (!string.Equals(completed.Path, relativePartPath, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Manifest path for {part.Year} does not match the expected annual part.");
                }

                if (!File.Exists(partPath))
                {
                    throw new FileNotFoundException($"The resumable historical part for {part.Year} is missing.", partPath);
                }

                detailPart = await ReadCsvTableAsync(partPath, cancellationToken);
                ValidatePartTable(detailPart, options, part);
                if (detailPart.Rows.Count != completed.RowCount)
                {
                    throw new InvalidDataException($"Historical part {part.Year} row count does not match its manifest.");
                }
            }
            else
            {
                var result = await client.ExecuteMdxAsync(
                    CatalogName,
                    BuildMdx(options, part),
                    debugOptions: DebugOptionsForYear(debugOptions, part.Year),
                    cancellationToken: cancellationToken);
                detailPart = NormalizePartTable(MdxTableBuilder.BuildTable(result), options, part);
                ValidatePartTable(detailPart, options, part);
                await WriteTextAtomicAsync(partPath, OutputFormatters.ToCsv(detailPart), cancellationToken);

                completedParts.RemoveAll(candidate => candidate.Year == part.Year);
                completedParts.Add(new MayoristasHistoricoPartManifest(
                    part.Year,
                    part.Months,
                    relativePartPath,
                    detailPart.Rows.Count));
                completedParts.Sort(static (left, right) => left.Year.CompareTo(right.Year));
                await WriteManifestAsync(
                    manifestPath,
                    CreateManifest(options, "in_progress", runTimestamp, equivalentCommand, completedParts, 0, 0),
                    cancellationToken);
            }

            detailTables.Add(detailPart);
            partPaths.Add(partPath);
        }

        var detailTable = CombineDetailTables(detailTables, options);
        var monthlyTable = BuildMonthlyTable(detailTable, options);
        var detailPath = Path.Combine(outputDirectory, "mayoristas-historico-detalle.csv");
        var monthlyPath = Path.Combine(outputDirectory, "mayoristas-historico-mensual.csv");
        await WriteTextAtomicAsync(detailPath, OutputFormatters.ToCsv(detailTable), cancellationToken);
        await WriteTextAtomicAsync(monthlyPath, OutputFormatters.ToCsv(monthlyTable), cancellationToken);
        await WriteManifestAsync(
            manifestPath,
            CreateManifest(
                options,
                "complete",
                runTimestamp,
                equivalentCommand,
                completedParts,
                detailTable.Rows.Count,
                monthlyTable.Rows.Count),
            cancellationToken);

        return new MayoristasHistoricoReportResult(
            options,
            outputDirectory,
            detailPath,
            monthlyPath,
            manifestPath,
            partPaths,
            detailTable,
            monthlyTable,
            equivalentCommand);
    }

    public static MdxTable NormalizePartTable(
        MdxTable table,
        MayoristasHistoricoReportOptions options,
        MayoristasHistoricoYearPart part)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var row in table.Rows)
        {
            var provider = Cell(row, 0).Trim();
            var month = ParseInt(Cell(row, 1));
            var year = ParseInt(Cell(row, 2));
            if (string.IsNullOrWhiteSpace(provider))
            {
                continue;
            }

            if (year != part.Year || month is null || !part.Months.Contains(month.Value))
            {
                throw new InvalidDataException($"OLAP returned a row outside the requested {part.Year} month range.");
            }

            if (!TryParseDecimal(Cell(row, 5), out var volume) || volume < 0)
            {
                continue;
            }

            rows.Add([
                BuildPeriod(year.Value, month.Value),
                year.Value.ToString(CultureInfo.InvariantCulture),
                month.Value.ToString(CultureInfo.InvariantCulture),
                provider,
                Cell(row, 3).Trim(),
                Cell(row, 4).Trim(),
                FormatDecimal(volume)
            ]);
        }

        var ordered = rows
            .OrderBy(row => Cell(row, 0), StringComparer.Ordinal)
            .ThenBy(row => Cell(row, 3), StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => Cell(row, 4), StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => Cell(row, 5), StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new MdxTable(DetailHeaders(options.Measure), ordered, table.Note);
    }

    public static MdxTable BuildMonthlyTable(
        MdxTable detailTable,
        MayoristasHistoricoReportOptions options)
    {
        var periodIndex = HeaderIndex(detailTable, "PERIODO");
        var yearIndex = HeaderIndex(detailTable, "ANO");
        var monthIndex = HeaderIndex(detailTable, "MES");
        var providerIndex = HeaderIndex(detailTable, "NOMBRE");
        var volumeIndex = HeaderIndex(detailTable, VolumeHeader(options.Measure));

        var grouped = detailTable.Rows
            .Select(row => new HistoricalDetailRow(
                Cell(row, periodIndex),
                ParseRequiredInt(Cell(row, yearIndex), "ANO"),
                ParseRequiredInt(Cell(row, monthIndex), "MES"),
                Cell(row, providerIndex),
                ParseRequiredNonNegativeDecimal(Cell(row, volumeIndex), VolumeHeader(options.Measure))))
            .GroupBy(row => new
            {
                row.Period,
                row.Year,
                row.Month,
                ProviderKey = row.Provider.Trim().ToUpperInvariant()
            })
            .Select(group => new HistoricalMonthlyRow(
                group.Key.Period,
                group.Key.Year,
                group.Key.Month,
                group.First().Provider.Trim(),
                group.Sum(static row => row.Volume)))
            .ToList();

        var totals = grouped
            .GroupBy(static row => (row.Year, row.Month))
            .ToDictionary(static group => group.Key, static group => group.Sum(row => row.Volume));
        foreach (var part in options.YearParts)
        {
            foreach (var month in part.Months)
            {
                if (!totals.TryGetValue((part.Year, month), out var total) || total <= 0)
                {
                    throw new InvalidDataException($"Historical data is missing or totals zero for {BuildPeriod(part.Year, month)}.");
                }
            }
        }

        var rows = grouped
            .OrderBy(static row => row.Year)
            .ThenBy(static row => row.Month)
            .ThenByDescending(static row => row.Volume)
            .ThenBy(static row => row.Provider, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
            {
                var participation = row.Volume * 100 / totals[(row.Year, row.Month)];
                return (IReadOnlyList<string>)[
                    row.Period,
                    row.Year.ToString(CultureInfo.InvariantCulture),
                    row.Month.ToString(CultureInfo.InvariantCulture),
                    row.Provider,
                    FormatDecimal(row.Volume),
                    FormatDecimal(participation)
                ];
            })
            .ToList();

        return new MdxTable(
            ["PERIODO", "ANO", "MES", "NOMBRE", VolumeHeader(options.Measure), "PARTICIPACION_TOTAL"],
            rows,
            null);
    }

    public static string BuildEquivalentCommand(
        MayoristasHistoricoReportOptions options,
        string outputDirectory)
    {
        var arguments = new List<string>
        {
            "dotnet run -- report mayoristas_historico",
            $"--start-year {options.StartYear}",
            $"--start-month {options.StartMonth}",
            $"--end-year {options.EndYear}",
            $"--end-month {options.EndMonth}",
            $"--measure {MeasureArgument(options.Measure)}",
            $"--product {ProductArgument(options.Product)}",
            $"--buyer-scope {BuyerScopeArgument(options.BuyerScope)}"
        };
        if (options.Resume)
        {
            arguments.Add("--resume");
        }

        arguments.Add($"--output-dir \"{outputDirectory}\"");
        return string.Join(" ", arguments);
    }

    public static void ValidateManifestCompatibility(
        MayoristasHistoricoManifest manifest,
        MayoristasHistoricoReportOptions options)
    {
        var expected = CreateManifest(options, "in_progress", manifest.GeneratedAtUtc, "", [], 0, 0);
        var compatible = manifest.SchemaVersion == expected.SchemaVersion &&
                         string.Equals(manifest.Report, expected.Report, StringComparison.Ordinal) &&
                         string.Equals(manifest.Catalog, expected.Catalog, StringComparison.Ordinal) &&
                         string.Equals(manifest.Cube, expected.Cube, StringComparison.Ordinal) &&
                         string.Equals(manifest.Measure, expected.Measure, StringComparison.Ordinal) &&
                         string.Equals(manifest.Product, expected.Product, StringComparison.Ordinal) &&
                         string.Equals(manifest.BuyerScope, expected.BuyerScope, StringComparison.Ordinal) &&
                         string.Equals(manifest.ProviderType, expected.ProviderType, StringComparison.Ordinal) &&
                         string.Equals(manifest.StartPeriod, expected.StartPeriod, StringComparison.Ordinal) &&
                         string.Equals(manifest.EndPeriod, expected.EndPeriod, StringComparison.Ordinal);
        if (!compatible)
        {
            throw new InvalidDataException("The existing historical manifest is incompatible with the requested parameters.");
        }
    }

    private static MayoristasHistoricoManifest CreateManifest(
        MayoristasHistoricoReportOptions options,
        string status,
        string generatedAtUtc,
        string equivalentCommand,
        IReadOnlyList<MayoristasHistoricoPartManifest> parts,
        int detailRowCount,
        int monthlyRowCount)
    {
        return new MayoristasHistoricoManifest(
            ManifestSchemaVersion,
            "mayoristas_historico",
            status,
            CatalogName,
            CubeName,
            MeasureArgument(options.Measure),
            ProductArgument(options.Product),
            BuyerScopeArgument(options.BuyerScope),
            ProviderType,
            BuildPeriod(options.StartYear, options.StartMonth),
            BuildPeriod(options.EndYear, options.EndMonth),
            generatedAtUtc,
            equivalentCommand,
            parts.OrderBy(static part => part.Year).ToList(),
            detailRowCount,
            monthlyRowCount);
    }

    private static MdxTable CombineDetailTables(
        IReadOnlyList<MdxTable> tables,
        MayoristasHistoricoReportOptions options)
    {
        var headers = DetailHeaders(options.Measure);
        foreach (var table in tables)
        {
            if (!table.Headers.SequenceEqual(headers, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Historical part headers are inconsistent.");
            }
        }

        var rows = tables
            .SelectMany(static table => table.Rows)
            .OrderBy(row => Cell(row, 0), StringComparer.Ordinal)
            .ThenBy(row => Cell(row, 3), StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => Cell(row, 4), StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => Cell(row, 5), StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new MdxTable(headers, rows, null);
    }

    private static void ValidatePartTable(
        MdxTable table,
        MayoristasHistoricoReportOptions options,
        MayoristasHistoricoYearPart part)
    {
        if (!table.Headers.SequenceEqual(DetailHeaders(options.Measure), StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Historical part {part.Year} has an invalid CSV contract.");
        }

        var yearIndex = HeaderIndex(table, "ANO");
        var monthIndex = HeaderIndex(table, "MES");
        var volumeIndex = HeaderIndex(table, VolumeHeader(options.Measure));
        foreach (var row in table.Rows)
        {
            var year = ParseRequiredInt(Cell(row, yearIndex), "ANO");
            var month = ParseRequiredInt(Cell(row, monthIndex), "MES");
            _ = ParseRequiredNonNegativeDecimal(Cell(row, volumeIndex), VolumeHeader(options.Measure));
            if (year != part.Year || !part.Months.Contains(month))
            {
                throw new InvalidDataException($"Historical part {part.Year} contains an out-of-range row.");
            }
        }
    }

    private static async Task<MdxTable> ReadCsvTableAsync(string path, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        var records = ParseCsv(text);
        if (records.Count == 0)
        {
            throw new InvalidDataException($"CSV is empty: {path}");
        }

        return new MdxTable(records[0], records.Skip(1).Cast<IReadOnlyList<string>>().ToList(), null);
    }

    private static IReadOnlyList<IReadOnlyList<string>> ParseCsv(string text)
    {
        var records = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (inQuotes)
            {
                if (character == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            if (character == '"')
            {
                inQuotes = true;
            }
            else if (character == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                row.Add(field.ToString());
                field.Clear();
                if (row.Count > 1 || row.Any(static value => !string.IsNullOrEmpty(value)))
                {
                    records.Add(row);
                }

                row = [];
            }
            else
            {
                field.Append(character);
            }
        }

        if (inQuotes)
        {
            throw new InvalidDataException("CSV contains an unterminated quoted field.");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            records.Add(row);
        }

        return records;
    }

    private static async Task WriteManifestAsync(
        string path,
        MayoristasHistoricoManifest manifest,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(manifest, ManifestJsonOptions) + Environment.NewLine;
        await WriteTextAtomicAsync(path, json, cancellationToken);
    }

    private static async Task WriteTextAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false), cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static XmlaDebugOptions? DebugOptionsForYear(XmlaDebugOptions? options, int year)
    {
        if (options is null)
        {
            return null;
        }

        return new XmlaDebugOptions(
            AppendYear(options.RequestOutputPath, year),
            AppendYear(options.ResponseOutputPath, year));
    }

    private static string? AppendYear(string? path, int year)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var extension = Path.GetExtension(path);
        var stem = extension.Length == 0 ? path : path[..^extension.Length];
        return $"{stem}-{year:0000}{extension}";
    }

    private static IReadOnlyList<string> DetailHeaders(MayoristasMeasureSelection measure)
    {
        return ["PERIODO", "ANO", "MES", "NOMBRE", "PRODUCTO", "SUBTIPO_AGENTE", VolumeHeader(measure)];
    }

    private static string VolumeHeader(MayoristasMeasureSelection measure)
    {
        return measure switch
        {
            MayoristasMeasureSelection.Accepted => "VOLUMEN_ACEPTADO",
            MayoristasMeasureSelection.Dispatched => "VOLUMEN_DESPACHADO",
            _ => throw new ArgumentOutOfRangeException(nameof(measure), measure, null)
        };
    }

    private static string MeasureUniqueName(MayoristasMeasureSelection measure)
    {
        return measure switch
        {
            MayoristasMeasureSelection.Accepted => "[Measures].[VOLUMEN ACEPTADO]",
            MayoristasMeasureSelection.Dispatched => "[Measures].[VOLUMEN DESPACHADO]",
            _ => throw new ArgumentOutOfRangeException(nameof(measure), measure, null)
        };
    }

    private static IReadOnlyList<string> ProductUniqueNames(MayoristasProductSelection product)
    {
        return product switch
        {
            MayoristasProductSelection.All => [
                "BIODIESEL CON MEZCLA",
                "GASOLINA MOTOR CORRIENTE",
                "GASOLINA MOTOR EXTRA"
            ],
            MayoristasProductSelection.Corriente => ["GASOLINA MOTOR CORRIENTE"],
            MayoristasProductSelection.Extra => ["GASOLINA MOTOR EXTRA"],
            MayoristasProductSelection.Diesel => ["BIODIESEL CON MEZCLA"],
            _ => throw new ArgumentOutOfRangeException(nameof(product), product, null)
        };
    }

    private static IReadOnlyList<string> BuyerSubtypeUniqueNames(EdsBuyerScope buyerScope)
    {
        return buyerScope switch
        {
            EdsBuyerScope.Eds => ["ESTACION DE SERVICIO AUTOMOTRIZ"],
            EdsBuyerScope.EdsFluvial => [
                "ESTACION DE SERVICIO AUTOMOTRIZ",
                "ESTACION DE SERVICIO FLUVIAL"
            ],
            EdsBuyerScope.EdsFluvialIndustrial => [
                "ESTACION DE SERVICIO AUTOMOTRIZ",
                "ESTACION DE SERVICIO FLUVIAL",
                "COMERCIALIZADOR INDUSTRIAL"
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(buyerScope), buyerScope, null)
        };
    }

    private static string MeasureArgument(MayoristasMeasureSelection measure)
    {
        return measure switch
        {
            MayoristasMeasureSelection.Accepted => "accepted",
            MayoristasMeasureSelection.Dispatched => "dispatched",
            _ => throw new ArgumentOutOfRangeException(nameof(measure), measure, null)
        };
    }

    private static string ProductArgument(MayoristasProductSelection product)
    {
        return product switch
        {
            MayoristasProductSelection.All => "all",
            MayoristasProductSelection.Corriente => "corriente",
            MayoristasProductSelection.Extra => "extra",
            MayoristasProductSelection.Diesel => "diesel",
            _ => throw new ArgumentOutOfRangeException(nameof(product), product, null)
        };
    }

    private static string BuyerScopeArgument(EdsBuyerScope buyerScope)
    {
        return buyerScope switch
        {
            EdsBuyerScope.Eds => "eds",
            EdsBuyerScope.EdsFluvial => "eds_fluvial",
            EdsBuyerScope.EdsFluvialIndustrial => "eds_fluvial_industrial",
            _ => throw new ArgumentOutOfRangeException(nameof(buyerScope), buyerScope, null)
        };
    }

    private static string BuildPeriod(int year, int month)
    {
        return $"{year:0000}-{month:00}";
    }

    private static int HeaderIndex(MdxTable table, string header)
    {
        var index = table.Headers
            .Select((value, itemIndex) => new { value, itemIndex })
            .FirstOrDefault(item => string.Equals(item.value, header, StringComparison.OrdinalIgnoreCase))
            ?.itemIndex;
        return index ?? throw new InvalidDataException($"Table does not contain header '{header}'.");
    }

    private static string Cell(IReadOnlyList<string> row, int index)
    {
        return index < row.Count ? row[index] : "";
    }

    private static int? ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static int ParseRequiredInt(string value, string field)
    {
        return ParseInt(value) ?? throw new InvalidDataException($"{field} is not a valid integer: '{value}'.");
    }

    private static decimal ParseRequiredNonNegativeDecimal(string value, string field)
    {
        if (!TryParseDecimal(value, out var parsed) || parsed < 0)
        {
            throw new InvalidDataException($"{field} is not a non-negative number: '{value}'.");
        }

        return parsed;
    }

    private static bool TryParseDecimal(string value, out decimal parsed)
    {
        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
        {
            return true;
        }

        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.GetCultureInfo("es-CO"), out parsed);
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    private static void ValidateMonth(int month, string optionName)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(optionName, $"{optionName} must be between 1 and 12.");
        }
    }

    private sealed record HistoricalDetailRow(
        string Period,
        int Year,
        int Month,
        string Provider,
        decimal Volume);

    private sealed record HistoricalMonthlyRow(
        string Period,
        int Year,
        int Month,
        string Provider,
        decimal Volume);
}
