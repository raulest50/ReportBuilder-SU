using System.Globalization;
using System.Text;

namespace WinSbi.Olap.Core;

public static class EdsInsightsReport
{
    public const string CatalogName = MayoristasReport.CatalogName;
    public const string CubeName = MayoristasReport.CubeName;
    public const string AgentsCatalogName = EdsMunicipiosReport.AgentsCatalogName;
    public const string AgentsCubeName = EdsMunicipiosReport.AgentsCubeName;

    public static EdsInsightsReportOptions CreateOptions(
        int year,
        int? quarter,
        int? semester,
        string? months,
        string product,
        int topCities,
        int topEds)
    {
        var period = ReportPeriods.Resolve(year, quarter, semester, months);

        if (topCities < 1)
        {
            throw new ArgumentException("--top-cities must be greater than zero.");
        }

        if (topEds < 1)
        {
            throw new ArgumentException("--top-eds must be greater than zero.");
        }

        return new EdsInsightsReportOptions(
            period.Year,
            period.Months,
            period.Label,
            period.CommandPart,
            MayoristasReport.ParseProduct(product),
            topCities,
            topEds);
    }

    public static EdsInsightsReportOptions CreateOptions(
        int year,
        int? quarter,
        string? months,
        string product,
        int topCities,
        int topEds)
    {
        return CreateOptions(year, quarter, null, months, product, topCities, topEds);
    }

    public static string DefaultOutputDirectory(EdsInsightsReportOptions options)
    {
        return Path.Combine("reports", CatalogName, "eds_insights", options.PeriodLabel);
    }

    public static async Task<EdsInsightsReportResult> GenerateAsync(
        IXmlaClient client,
        EdsInsightsReportOptions options,
        string outputDirectory,
        string equivalentCommand,
        XmlaDebugOptions? debugOptions = null,
        CancellationToken cancellationToken = default)
    {
        var nationalFlagsResult = await client.ExecuteMdxAsync(
            AgentsCatalogName,
            BuildActiveFlagsNationalMdx(),
            debugOptions: AddDebugSuffix(debugOptions, "-banderas-nacional"),
            cancellationToken: cancellationToken);
        var nationalFlagsTable = NormalizeNationalFlagsTable(MdxTableBuilder.BuildTable(nationalFlagsResult));

        var topCitiesResult = await client.ExecuteMdxAsync(
            CatalogName,
            BuildTopCitiesMdx(options),
            debugOptions: AddDebugSuffix(debugOptions, "-ciudades-top"),
            cancellationToken: cancellationToken);
        var topCityRows = NormalizeTopCityRows(MdxTableBuilder.BuildTable(topCitiesResult), options.TopCities);

        var topCityFlagsTable = EmptyTopCityFlagsTable();
        if (topCityRows.Count > 0)
        {
            var topCityFlagsResult = await client.ExecuteMdxAsync(
                AgentsCatalogName,
                BuildTopCityFlagsMdx(topCityRows),
                debugOptions: AddDebugSuffix(debugOptions, "-banderas-ciudades-top"),
                cancellationToken: cancellationToken);
            topCityFlagsTable = BuildTopCityFlagsTable(MdxTableBuilder.BuildTable(topCityFlagsResult), topCityRows);
        }

        var topEdsResult = await client.ExecuteMdxAsync(
            CatalogName,
            BuildTopEdsMdx(options),
            debugOptions: AddDebugSuffix(debugOptions, "-top-eds"),
            cancellationToken: cancellationToken);
        var topEdsRows = NormalizeTopEdsRows(MdxTableBuilder.BuildTable(topEdsResult), options.TopEds);

        var topEdsFlags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dominantSuppliers = new Dictionary<string, DominantSupplier>(StringComparer.OrdinalIgnoreCase);
        if (topEdsRows.Count > 0)
        {
            var topEdsFlagsResult = await client.ExecuteMdxAsync(
                AgentsCatalogName,
                BuildTopEdsFlagsMdx(topEdsRows.Select(static row => row.Code).ToList()),
                debugOptions: AddDebugSuffix(debugOptions, "-top-eds-banderas"),
                cancellationToken: cancellationToken);
            topEdsFlags = BuildCodeFlagMap(MdxTableBuilder.BuildTable(topEdsFlagsResult));

            var dominantSupplierResult = await client.ExecuteMdxAsync(
                CatalogName,
                BuildDominantSupplierMdx(options, topEdsRows.Select(static row => row.Code).ToList()),
                debugOptions: AddDebugSuffix(debugOptions, "-top-eds-proveedor-dominante"),
                cancellationToken: cancellationToken);
            dominantSuppliers = BuildDominantSupplierMap(MdxTableBuilder.BuildTable(dominantSupplierResult), topEdsRows);
        }

        var borderVolumesResult = await client.ExecuteMdxAsync(
            CatalogName,
            BuildBorderVolumesMdx(options),
            debugOptions: AddDebugSuffix(debugOptions, "-frontera-volumenes"),
            cancellationToken: cancellationToken);
        var borderVolumes = NormalizeBorderVolumeRows(MdxTableBuilder.BuildTable(borderVolumesResult));

        var borderActiveResult = await client.ExecuteMdxAsync(
            AgentsCatalogName,
            BuildBorderActiveEdsMdx(),
            debugOptions: AddDebugSuffix(debugOptions, "-frontera-eds-activas"),
            cancellationToken: cancellationToken);
        var borderActiveRows = NormalizeBorderActiveRows(MdxTableBuilder.BuildTable(borderActiveResult));

        var nationalAccepted = borderVolumes
            .Where(row => row.Year == options.Year)
            .Sum(static row => row.Accepted);
        var topCitiesTable = BuildTopCitiesTable(topCityRows, nationalAccepted);
        var topEdsTable = BuildTopEdsTable(topEdsRows, topEdsFlags, dominantSuppliers, nationalAccepted);
        var borderSummaryTable = BuildBorderSummaryTable(borderVolumes, borderActiveRows, options);
        var borderProductsTable = BuildBorderProductsTable(borderVolumes, options);

        Directory.CreateDirectory(outputDirectory);
        var nationalFlagsPath = Path.Combine(outputDirectory, "eds-banderas-nacional.csv");
        var topCitiesPath = Path.Combine(outputDirectory, "eds-ciudades-top.csv");
        var topCityFlagsPath = Path.Combine(outputDirectory, "eds-banderas-ciudades-top.csv");
        var topEdsPath = Path.Combine(outputDirectory, "eds-top20-volumen.csv");
        var borderSummaryPath = Path.Combine(outputDirectory, "eds-frontera-resumen.csv");
        var borderProductsPath = Path.Combine(outputDirectory, "eds-frontera-productos.csv");

        await File.WriteAllTextAsync(nationalFlagsPath, OutputFormatters.ToCsv(nationalFlagsTable), cancellationToken);
        await File.WriteAllTextAsync(topCitiesPath, OutputFormatters.ToCsv(topCitiesTable), cancellationToken);
        await File.WriteAllTextAsync(topCityFlagsPath, OutputFormatters.ToCsv(topCityFlagsTable), cancellationToken);
        await File.WriteAllTextAsync(topEdsPath, OutputFormatters.ToCsv(topEdsTable), cancellationToken);
        await File.WriteAllTextAsync(borderSummaryPath, OutputFormatters.ToCsv(borderSummaryTable), cancellationToken);
        await File.WriteAllTextAsync(borderProductsPath, OutputFormatters.ToCsv(borderProductsTable), cancellationToken);

        return new EdsInsightsReportResult(
            options,
            outputDirectory,
            nationalFlagsPath,
            topCitiesPath,
            topCityFlagsPath,
            topEdsPath,
            borderSummaryPath,
            borderProductsPath,
            nationalFlagsTable,
            topCitiesTable,
            topCityFlagsTable,
            topEdsTable,
            borderSummaryTable,
            borderProductsTable,
            equivalentCommand);
    }

    public static string BuildActiveFlagsNationalMdx()
    {
        return $$"""
WITH
SET [target_banderas] AS
    ORDER(
        NONEMPTY(
            EXCEPT(
                [BANDERA].[BANDERA].MEMBERS,
                [BANDERA].[BANDERA].[ALL]
            ),
            [Measures].[AGENTE CANTIDAD]
        ),
        [Measures].[AGENTE CANTIDAD],
        BDESC
    )
SELECT
    {
        [Measures].[AGENTE CANTIDAD],
        [Measures].[REGISTROS CANTIDAD]
    } ON COLUMNS,
    [target_banderas] ON ROWS
FROM [{{AgentsCubeName}}]
WHERE (
    [SUBTIPO AGENTE].[SUBTIPO AGENTE].&[ESTACION DE SERVICIO AUTOMOTRIZ],
    [AGENTE].[NUEVO ESTADO].&[ACTIVO]
)
""";
    }

    public static string BuildTopCitiesMdx(EdsInsightsReportOptions options)
    {
        var monthMembers = MonthMembers(options);
        var productMembers = ProductMembers(options.Product);

        return $$"""
WITH
SET [target_months] AS {
{{monthMembers}}
}
SET [target_products] AS {
{{productMembers}}
}
MEMBER [Measures].[VOLUMEN ACEPTADO PERIODO] AS
    SUM([target_months] * [target_products], [Measures].[VOLUMEN ACEPTADO])
MEMBER [Measures].[VOLUMEN DESPACHADO PERIODO] AS
    SUM([target_months] * [target_products], [Measures].[VOLUMEN DESPACHADO])
SET [target_rows] AS
    TOPCOUNT(
        NONEMPTY(
            [COMPRADOR].[CODIGO DANE DEPARTAMENTO AGENTE COMPRADOR].[CODIGO DANE DEPARTAMENTO AGENTE COMPRADOR].Members
            * [COMPRADOR].[DEPARTAMENTO AGENTE COMPRADOR].[DEPARTAMENTO AGENTE COMPRADOR].Members
            * [COMPRADOR].[CODIGO DANE MUNICIPIO AGENTE COMPRADOR].[CODIGO DANE MUNICIPIO AGENTE COMPRADOR].Members
            * [COMPRADOR].[MUNICIPIO AGENTE COMPRADOR].[MUNICIPIO AGENTE COMPRADOR].Members,
            [Measures].[VOLUMEN ACEPTADO PERIODO]
        ),
        {{options.TopCities}},
        [Measures].[VOLUMEN ACEPTADO PERIODO]
    )
SELECT
    {
        [Measures].[VOLUMEN ACEPTADO PERIODO],
        [Measures].[VOLUMEN DESPACHADO PERIODO]
    } ON COLUMNS,
    [target_rows] ON ROWS
FROM [{{CubeName}}]
WHERE (
    [MOVIMIENTOS ORDEN PEDIDO].[AÑO DESPACHO].&[{{options.Year}}],
    [COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[ESTACION DE SERVICIO AUTOMOTRIZ]
)
""";
    }

    public static string BuildTopCityFlagsMdx(IReadOnlyList<CityRow> topCities)
    {
        var cityMembers = string.Join($",{Environment.NewLine}", topCities.Select(static city =>
            $"    [UBICACION INSTALACION].[CODIGO DANE MUNICIPIO INSTALACION].&[{city.MunicipalityCode}]"));

        return $$"""
WITH
SET [target_cities] AS {
{{cityMembers}}
}
SET [rows] AS
    NONEMPTY(
        [UBICACION INSTALACION].[CODIGO DANE DEPARTAMENTO INSTALACION].[CODIGO DANE DEPARTAMENTO INSTALACION].Members
        * [UBICACION INSTALACION].[DEPARTAMENTO INSTALACION].[DEPARTAMENTO INSTALACION].Members
        * [target_cities]
        * [UBICACION INSTALACION].[MUNICIPIO INSTALACION].[MUNICIPIO INSTALACION].Members
        * [BANDERA].[BANDERA].[BANDERA].Members,
        [Measures].[AGENTE CANTIDAD]
    )
SELECT
    {
        [Measures].[AGENTE CANTIDAD],
        [Measures].[REGISTROS CANTIDAD]
    } ON COLUMNS,
    [rows] ON ROWS
FROM [{{AgentsCubeName}}]
WHERE (
    [SUBTIPO AGENTE].[SUBTIPO AGENTE].&[ESTACION DE SERVICIO AUTOMOTRIZ],
    [AGENTE].[NUEVO ESTADO].&[ACTIVO]
)
""";
    }

    public static string BuildTopEdsMdx(EdsInsightsReportOptions options)
    {
        var monthMembers = MonthMembers(options);
        var productMembers = ProductMembers(options.Product);

        return $$"""
WITH
SET [target_months] AS {
{{monthMembers}}
}
SET [target_products] AS {
{{productMembers}}
}
MEMBER [Measures].[VOLUMEN ACEPTADO PERIODO] AS
    SUM([target_months] * [target_products], [Measures].[VOLUMEN ACEPTADO])
MEMBER [Measures].[VOLUMEN DESPACHADO PERIODO] AS
    SUM([target_months] * [target_products], [Measures].[VOLUMEN DESPACHADO])
SET [target_rows] AS
    TOPCOUNT(
        NONEMPTY(
            [COMPRADOR].[CODIGO SICOM COMPRADOR].[CODIGO SICOM COMPRADOR].Members
            * [COMPRADOR].[NOMBRE COMERCIAL COMPRADOR].[NOMBRE COMERCIAL COMPRADOR].Members
            * [COMPRADOR].[MUNICIPIO AGENTE COMPRADOR].[MUNICIPIO AGENTE COMPRADOR].Members
            * [COMPRADOR].[DEPARTAMENTO AGENTE COMPRADOR].[DEPARTAMENTO AGENTE COMPRADOR].Members
            * [COMPRADOR].[ZONA FRONTERA].[ZONA FRONTERA].Members,
            [Measures].[VOLUMEN ACEPTADO PERIODO]
        ),
        {{options.TopEds}},
        [Measures].[VOLUMEN ACEPTADO PERIODO]
    )
SELECT
    {
        [Measures].[VOLUMEN ACEPTADO PERIODO],
        [Measures].[VOLUMEN DESPACHADO PERIODO]
    } ON COLUMNS,
    [target_rows] ON ROWS
FROM [{{CubeName}}]
WHERE (
    [MOVIMIENTOS ORDEN PEDIDO].[AÑO DESPACHO].&[{{options.Year}}],
    [COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[ESTACION DE SERVICIO AUTOMOTRIZ]
)
""";
    }

    public static string BuildTopEdsFlagsMdx(IReadOnlyList<string> codes)
    {
        var codeMembers = string.Join($",{Environment.NewLine}", codes.Distinct(StringComparer.OrdinalIgnoreCase).Select(static code =>
            $"    [AGENTE].[CODIGO SICOM AGENTE].&[{code}]"));

        return $$"""
WITH
SET [target_agents] AS {
{{codeMembers}}
}
SET [rows] AS
    NONEMPTY(
        [target_agents]
        * [BANDERA].[BANDERA].[BANDERA].Members,
        [Measures].[AGENTE CANTIDAD]
    )
SELECT
    { [Measures].[AGENTE CANTIDAD] } ON COLUMNS,
    [rows] ON ROWS
FROM [{{AgentsCubeName}}]
WHERE (
    [SUBTIPO AGENTE].[SUBTIPO AGENTE].&[ESTACION DE SERVICIO AUTOMOTRIZ],
    [AGENTE].[NUEVO ESTADO].&[ACTIVO]
)
""";
    }

    public static string BuildDominantSupplierMdx(EdsInsightsReportOptions options, IReadOnlyList<string> codes)
    {
        var monthMembers = MonthMembers(options);
        var productMembers = ProductMembers(options.Product);
        var codeMembers = string.Join($",{Environment.NewLine}", codes.Distinct(StringComparer.OrdinalIgnoreCase).Select(static code =>
            $"    [COMPRADOR].[CODIGO SICOM COMPRADOR].&[{code}]"));

        return $$"""
WITH
SET [target_months] AS {
{{monthMembers}}
}
SET [target_products] AS {
{{productMembers}}
}
SET [target_buyers] AS {
{{codeMembers}}
}
MEMBER [Measures].[VOLUMEN ACEPTADO PERIODO] AS
    SUM([target_months] * [target_products], [Measures].[VOLUMEN ACEPTADO])
SET [rows] AS
    NONEMPTY(
        [target_buyers]
        * [PROVEEDOR].[NOMBRE COMERCIAL PROVEEDOR].[NOMBRE COMERCIAL PROVEEDOR].Members
        * [PLANTA PROVEEDOR].[PLANTA AGENTE PROVEEDOR].[PLANTA AGENTE PROVEEDOR].Members,
        [Measures].[VOLUMEN ACEPTADO PERIODO]
    )
SELECT
    { [Measures].[VOLUMEN ACEPTADO PERIODO] } ON COLUMNS,
    [rows] ON ROWS
FROM [{{CubeName}}]
WHERE (
    [MOVIMIENTOS ORDEN PEDIDO].[AÑO DESPACHO].&[{{options.Year}}],
    [COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[ESTACION DE SERVICIO AUTOMOTRIZ]
)
""";
    }

    public static string BuildBorderVolumesMdx(EdsInsightsReportOptions options)
    {
        var monthMembers = MonthMembers(options);
        var productMembers = ProductMembers(options.Product);

        return $$"""
WITH
SET [target_months] AS {
{{monthMembers}}
}
SET [target_products] AS {
{{productMembers}}
}
SET [target_years] AS {
    [MOVIMIENTOS ORDEN PEDIDO].[AÑO DESPACHO].&[{{options.Year - 1}}],
    [MOVIMIENTOS ORDEN PEDIDO].[AÑO DESPACHO].&[{{options.Year}}]
}
MEMBER [Measures].[VOLUMEN ACEPTADO PERIODO] AS
    SUM([target_months], [Measures].[VOLUMEN ACEPTADO])
MEMBER [Measures].[VOLUMEN DESPACHADO PERIODO] AS
    SUM([target_months], [Measures].[VOLUMEN DESPACHADO])
SET [rows] AS
    NONEMPTY(
        [COMPRADOR].[ZONA FRONTERA].[ZONA FRONTERA].Members
        * [target_years]
        * [target_products],
        [Measures].[VOLUMEN ACEPTADO PERIODO]
    )
SELECT
    {
        [Measures].[VOLUMEN ACEPTADO PERIODO],
        [Measures].[VOLUMEN DESPACHADO PERIODO]
    } ON COLUMNS,
    [rows] ON ROWS
FROM [{{CubeName}}]
WHERE ([COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[ESTACION DE SERVICIO AUTOMOTRIZ])
""";
    }

    public static string BuildBorderActiveEdsMdx()
    {
        return $$"""
WITH
SET [rows] AS
    NONEMPTY(
        [AGENTE].[ZONA DE FRONTERA].[ZONA DE FRONTERA].Members,
        [Measures].[AGENTE CANTIDAD]
    )
SELECT
    { [Measures].[AGENTE CANTIDAD] } ON COLUMNS,
    [rows] ON ROWS
FROM [{{AgentsCubeName}}]
WHERE (
    [SUBTIPO AGENTE].[SUBTIPO AGENTE].&[ESTACION DE SERVICIO AUTOMOTRIZ],
    [AGENTE].[NUEVO ESTADO].&[ACTIVO]
)
""";
    }

    public static MdxTable NormalizeNationalFlagsTable(MdxTable table)
    {
        var rows = table.Rows
            .Select(row => new
            {
                Flag = CleanText(Cell(row, 0)),
                Active = ParseDecimal(Cell(row, 1)),
                Records = ParseDecimal(Cell(row, 2))
            })
            .Where(row => !IsAllMember(row.Flag) && !string.IsNullOrWhiteSpace(row.Flag))
            .GroupBy(row => row.Flag, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Flag = group.Key,
                Active = group.Sum(row => row.Active),
                Records = group.Sum(row => row.Records)
            })
            .OrderByDescending(row => row.Active)
            .ToList();
        var total = rows.Sum(static row => row.Active);

        return new MdxTable(
            ["BANDERA", "EDS_ACTIVAS", "REGISTROS_CANTIDAD", "PARTICIPACION_EDS_NACIONAL_PCT"],
            rows.Select(row => new[]
                {
                    row.Flag,
                    FormatDecimal(row.Active),
                    FormatDecimal(row.Records),
                    FormatPercentage(Divide(row.Active * 100, total))
                })
                .Cast<IReadOnlyList<string>>()
                .ToList(),
            table.Note);
    }

    public static MdxTable BuildTopCitiesTable(IReadOnlyList<CityRow> cities, decimal nationalAccepted)
    {
        return new MdxTable(
            [
                "RANK_CIUDAD",
                "CODIGO_DANE_MUNICIPIO",
                "MUNICIPIO",
                "DEPARTAMENTO",
                "VOLUMEN_ACEPTADO",
                "VOLUMEN_DESPACHADO",
                "PARTICIPACION_VOLUMEN_NACIONAL_PCT"
            ],
            cities.Select(row => new[]
                {
                    row.Rank.ToString(CultureInfo.InvariantCulture),
                    row.MunicipalityCode,
                    row.Municipality,
                    row.Department,
                    FormatDecimal(row.Accepted),
                    FormatDecimal(row.Dispatched),
                    FormatPercentage(Divide(row.Accepted * 100, nationalAccepted))
                })
                .Cast<IReadOnlyList<string>>()
                .ToList(),
            null);
    }

    public static MdxTable BuildTopEdsTable(
        IReadOnlyList<TopEdsRow> edsRows,
        IReadOnlyDictionary<string, string> flagsByCode,
        IReadOnlyDictionary<string, DominantSupplier> suppliersByCode,
        decimal nationalAccepted)
    {
        return new MdxTable(
            [
                "RANK_EDS",
                "CODIGO_SICOM_COMPRADOR",
                "NOMBRE_COMERCIAL_COMPRADOR",
                "MUNICIPIO",
                "DEPARTAMENTO",
                "ZONA_FRONTERA",
                "BANDERA",
                "VOLUMEN_ACEPTADO",
                "VOLUMEN_DESPACHADO",
                "PARTICIPACION_NACIONAL_ACEPTADO_PCT",
                "PROVEEDOR_DOMINANTE",
                "PLANTA_PROVEEDOR_DOMINANTE",
                "PARTICIPACION_PROVEEDOR_DOMINANTE_PCT"
            ],
            edsRows.Select(row =>
                {
                    flagsByCode.TryGetValue(row.Code, out var flag);
                    suppliersByCode.TryGetValue(row.Code, out var supplier);
                    return new[]
                    {
                        row.Rank.ToString(CultureInfo.InvariantCulture),
                        row.Code,
                        row.Name,
                        row.Municipality,
                        row.Department,
                        row.BorderZone,
                        flag ?? "",
                        FormatDecimal(row.Accepted),
                        FormatDecimal(row.Dispatched),
                        FormatPercentage(Divide(row.Accepted * 100, nationalAccepted)),
                        supplier?.Provider ?? "",
                        supplier?.Plant ?? "",
                        supplier is null ? "" : FormatPercentage(Divide(supplier.Volume * 100, row.Accepted))
                    };
                })
                .Cast<IReadOnlyList<string>>()
                .ToList(),
            null);
    }

    public static MdxTable BuildBorderSummaryTable(
        IReadOnlyList<BorderVolumeRow> volumeRows,
        IReadOnlyList<BorderActiveRow> activeRows,
        EdsInsightsReportOptions options)
    {
        var currentRows = volumeRows.Where(row => row.Year == options.Year).ToList();
        var previousRows = volumeRows.Where(row => row.Year == options.Year - 1).ToList();
        var activeByZone = activeRows
            .GroupBy(static row => row.BorderFlag, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Sum(row => row.ActiveEds), StringComparer.OrdinalIgnoreCase);

        var nationalActive = activeByZone.Values.Sum();
        var nationalAccepted = currentRows.Sum(static row => row.Accepted);
        var nationalDispatched = currentRows.Sum(static row => row.Dispatched);
        var nationalPreviousAccepted = previousRows.Sum(static row => row.Accepted);
        var rows = new List<IReadOnlyList<string>>();

        foreach (var zone in new[] { "SI", "NO" })
        {
            var accepted = currentRows.Where(row => row.BorderFlag == zone).Sum(static row => row.Accepted);
            var dispatched = currentRows.Where(row => row.BorderFlag == zone).Sum(static row => row.Dispatched);
            var previousAccepted = previousRows.Where(row => row.BorderFlag == zone).Sum(static row => row.Accepted);
            activeByZone.TryGetValue(zone, out var activeEds);
            rows.Add(BorderSummaryRow(
                zone,
                activeEds,
                nationalActive,
                accepted,
                dispatched,
                nationalAccepted,
                previousAccepted,
                options.Months.Count));
        }

        rows.Add(BorderSummaryRow(
            "TOTAL NACIONAL",
            nationalActive,
            nationalActive,
            nationalAccepted,
            nationalDispatched,
            nationalAccepted,
            nationalPreviousAccepted,
            options.Months.Count));

        return new MdxTable(
            [
                "ZONA_FRONTERA",
                "EDS_ACTIVAS",
                "PARTICIPACION_EDS_NACIONAL_PCT",
                "VOLUMEN_ACEPTADO",
                "VOLUMEN_DESPACHADO",
                "PARTICIPACION_VOLUMEN_NACIONAL_PCT",
                "GAL_MES_EDS_ACEPTADO",
                "VOLUMEN_ACEPTADO_ANIO_ANTERIOR",
                "VAR_INTERANUAL_ACEPTADO_PCT"
            ],
            rows,
            null);
    }

    public static MdxTable BuildBorderProductsTable(
        IReadOnlyList<BorderVolumeRow> volumeRows,
        EdsInsightsReportOptions options)
    {
        var currentRows = volumeRows.Where(row => row.Year == options.Year).ToList();
        var zoneTotals = currentRows
            .GroupBy(static row => row.BorderFlag, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Sum(row => row.Accepted), StringComparer.OrdinalIgnoreCase);
        var productTotals = currentRows
            .GroupBy(static row => row.ProductCanonical, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Sum(row => row.Accepted), StringComparer.OrdinalIgnoreCase);

        var rows = currentRows
            .GroupBy(row => new ProductZoneKey(row.BorderFlag, row.Product, row.ProductCanonical))
            .Select(group =>
            {
                var accepted = group.Sum(row => row.Accepted);
                var dispatched = group.Sum(row => row.Dispatched);
                zoneTotals.TryGetValue(group.Key.BorderFlag, out var zoneTotal);
                productTotals.TryGetValue(group.Key.ProductCanonical, out var productTotal);
                return new[]
                {
                    group.Key.BorderFlag,
                    group.Key.Product,
                    group.Key.ProductCanonical,
                    FormatDecimal(accepted),
                    FormatDecimal(dispatched),
                    FormatPercentage(Divide(accepted * 100, zoneTotal)),
                    FormatPercentage(Divide(accepted * 100, productTotal))
                };
            })
            .OrderBy(row => row[0], StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row[2], StringComparer.OrdinalIgnoreCase)
            .Cast<IReadOnlyList<string>>()
            .ToList();

        return new MdxTable(
            [
                "ZONA_FRONTERA",
                "PRODUCTO",
                "PRODUCTO_CANONICO",
                "VOLUMEN_ACEPTADO",
                "VOLUMEN_DESPACHADO",
                "PARTICIPACION_PRODUCTO_EN_ZONA_PCT",
                "PARTICIPACION_ZONA_EN_PRODUCTO_NACIONAL_PCT"
            ],
            rows,
            null);
    }

    public static string BuildEquivalentCommand(EdsInsightsReportOptions options, string outputDirectory)
    {
        return string.Join(" ", [
            "dotnet run -- report eds_insights",
            $"--year {options.Year}",
            options.PeriodCommandPart,
            $"--product {ProductArgument(options.Product)}",
            $"--top-cities {options.TopCities}",
            $"--top-eds {options.TopEds}",
            $"--output-dir \"{outputDirectory}\""
        ]);
    }

    private static IReadOnlyList<CityRow> NormalizeTopCityRows(MdxTable table, int maxRows)
    {
        return table.Rows
            .Select(row => new CityRow(
                Rank: 0,
                DepartmentCode: Cell(row, 0),
                Department: Cell(row, 1),
                MunicipalityCode: Cell(row, 2),
                Municipality: Cell(row, 3),
                Accepted: ParseDecimal(Cell(row, 4)),
                Dispatched: ParseDecimal(Cell(row, 5))))
            .Where(static row => !string.IsNullOrWhiteSpace(row.MunicipalityCode) || !string.IsNullOrWhiteSpace(row.Municipality))
            .OrderByDescending(static row => row.Accepted)
            .Take(maxRows)
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
    }

    private static MdxTable BuildTopCityFlagsTable(MdxTable table, IReadOnlyList<CityRow> topCities)
    {
        var citiesByCode = topCities
            .Where(static city => !string.IsNullOrWhiteSpace(city.MunicipalityCode))
            .ToDictionary(static city => NormalizeKey(city.MunicipalityCode), static city => city, StringComparer.OrdinalIgnoreCase);

        var raw = table.Rows
            .Select(row =>
            {
                var code = Cell(row, 2);
                citiesByCode.TryGetValue(NormalizeKey(code), out var city);
                return new
                {
                    City = city,
                    MunicipalityCode = code,
                    Municipality = Cell(row, 3),
                    Department = Cell(row, 1),
                    Flag = CleanText(Cell(row, 4)),
                    Active = ParseDecimal(Cell(row, 5))
                };
            })
            .Where(row => row.City is not null && !IsAllMember(row.Flag) && !string.IsNullOrWhiteSpace(row.Flag))
            .GroupBy(row => new CityFlagKey(
                row.City!.Rank,
                row.City.MunicipalityCode,
                row.City.Municipality,
                row.City.Department,
                row.Flag))
            .Select(group => new
            {
                group.Key,
                Active = group.Sum(row => row.Active)
            })
            .ToList();
        var totalsByCity = raw
            .GroupBy(row => row.Key.MunicipalityCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Sum(row => row.Active), StringComparer.OrdinalIgnoreCase);

        return new MdxTable(
            [
                "RANK_CIUDAD",
                "CODIGO_DANE_MUNICIPIO",
                "MUNICIPIO",
                "DEPARTAMENTO",
                "BANDERA",
                "EDS_ACTIVAS",
                "PARTICIPACION_EDS_CIUDAD_PCT"
            ],
            raw.OrderBy(static row => row.Key.Rank)
                .ThenByDescending(static row => row.Active)
                .Select(row =>
                {
                    totalsByCity.TryGetValue(row.Key.MunicipalityCode, out var cityTotal);
                    return new[]
                    {
                        row.Key.Rank.ToString(CultureInfo.InvariantCulture),
                        row.Key.MunicipalityCode,
                        row.Key.Municipality,
                        row.Key.Department,
                        row.Key.Flag,
                        FormatDecimal(row.Active),
                        FormatPercentage(Divide(row.Active * 100, cityTotal))
                    };
                })
                .Cast<IReadOnlyList<string>>()
                .ToList(),
            table.Note);
    }

    private static MdxTable EmptyTopCityFlagsTable()
    {
        return new MdxTable(
            [
                "RANK_CIUDAD",
                "CODIGO_DANE_MUNICIPIO",
                "MUNICIPIO",
                "DEPARTAMENTO",
                "BANDERA",
                "EDS_ACTIVAS",
                "PARTICIPACION_EDS_CIUDAD_PCT"
            ],
            [],
            null);
    }

    private static IReadOnlyList<TopEdsRow> NormalizeTopEdsRows(MdxTable table, int maxRows)
    {
        return table.Rows
            .Select(row => new TopEdsRow(
                Rank: 0,
                Code: Cell(row, 0),
                Name: Cell(row, 1),
                Municipality: Cell(row, 2),
                Department: Cell(row, 3),
                BorderZone: BorderZoneFlag(Cell(row, 4)),
                Accepted: ParseDecimal(Cell(row, 5)),
                Dispatched: ParseDecimal(Cell(row, 6))))
            .Where(static row => !string.IsNullOrWhiteSpace(row.Code))
            .OrderByDescending(static row => row.Accepted)
            .Take(maxRows)
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
    }

    private static Dictionary<string, string> BuildCodeFlagMap(MdxTable table)
    {
        return table.Rows
            .Select(row => new
            {
                Code = Cell(row, 0),
                Flag = CleanText(Cell(row, 1)),
                Count = ParseDecimal(Cell(row, 2))
            })
            .Where(row => !string.IsNullOrWhiteSpace(row.Code) && !string.IsNullOrWhiteSpace(row.Flag) && !IsAllMember(row.Flag))
            .GroupBy(row => row.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderByDescending(row => row.Count).First().Flag,
                StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, DominantSupplier> BuildDominantSupplierMap(
        MdxTable table,
        IReadOnlyList<TopEdsRow> topEdsRows)
    {
        var acceptedByCode = topEdsRows.ToDictionary(static row => row.Code, static row => row.Accepted, StringComparer.OrdinalIgnoreCase);
        return table.Rows
            .Select(row => new
            {
                Code = Cell(row, 0),
                Provider = Cell(row, 1),
                Plant = Cell(row, 2),
                Volume = ParseDecimal(Cell(row, 3))
            })
            .Where(row => !string.IsNullOrWhiteSpace(row.Code))
            .GroupBy(row => row.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                group =>
                {
                    var best = group.OrderByDescending(row => row.Volume).First();
                    acceptedByCode.TryGetValue(best.Code, out var total);
                    return new DominantSupplier(best.Provider, best.Plant, best.Volume, total);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<BorderVolumeRow> NormalizeBorderVolumeRows(MdxTable table)
    {
        return table.Rows
            .Select(row => new BorderVolumeRow(
                BorderFlag: BorderZoneFlag(Cell(row, 0)),
                Year: ParseInt(Cell(row, 1)) ?? 0,
                Product: Cell(row, 2),
                ProductCanonical: ProductCanonical(Cell(row, 2)),
                Accepted: ParseDecimal(Cell(row, 3)),
                Dispatched: ParseDecimal(Cell(row, 4))))
            .Where(static row => row.Year > 0 && (row.BorderFlag == "SI" || row.BorderFlag == "NO"))
            .ToList();
    }

    private static IReadOnlyList<BorderActiveRow> NormalizeBorderActiveRows(MdxTable table)
    {
        return table.Rows
            .Select(row => new BorderActiveRow(
                BorderFlag: BorderZoneFlag(Cell(row, 0)),
                ActiveEds: ParseDecimal(Cell(row, 1))))
            .Where(static row => row.BorderFlag == "SI" || row.BorderFlag == "NO")
            .ToList();
    }

    private static IReadOnlyList<string> BorderSummaryRow(
        string zone,
        decimal activeEds,
        decimal nationalActive,
        decimal accepted,
        decimal dispatched,
        decimal nationalAccepted,
        decimal previousAccepted,
        int monthCount)
    {
        var galMonthEds = activeEds == 0 || monthCount == 0 ? "" : FormatDecimal(accepted / monthCount / activeEds);
        var yoy = previousAccepted == 0 ? "" : FormatPercentage((accepted - previousAccepted) * 100 / previousAccepted);
        return
        [
            zone,
            FormatDecimal(activeEds),
            FormatPercentage(Divide(activeEds * 100, nationalActive)),
            FormatDecimal(accepted),
            FormatDecimal(dispatched),
            FormatPercentage(Divide(accepted * 100, nationalAccepted)),
            galMonthEds,
            FormatDecimal(previousAccepted),
            yoy
        ];
    }

    private static string MonthMembers(EdsInsightsReportOptions options)
    {
        return string.Join($",{Environment.NewLine}", options.Months.Select(static month =>
            $"    [MOVIMIENTOS ORDEN PEDIDO].[MES DESPACHO].&[{month}]"));
    }

    private static string ProductMembers(MayoristasProductSelection product)
    {
        return string.Join($",{Environment.NewLine}", ProductUniqueNames(product).Select(static productName =>
            $"    [PRODUCTO].[DESCRIPCION PRODUCTO].&[{productName}]"));
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

    private static string ProductCanonical(string product)
    {
        var normalized = NormalizeInvariant(product);
        if (normalized.Contains("CORRIENTE", StringComparison.OrdinalIgnoreCase))
        {
            return "corriente";
        }

        if (normalized.Contains("EXTRA", StringComparison.OrdinalIgnoreCase))
        {
            return "extra";
        }

        if (normalized.Contains("BIODIESEL", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("DIESEL", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("ACPM", StringComparison.OrdinalIgnoreCase))
        {
            return "diesel";
        }

        return product.Trim().ToLowerInvariant();
    }

    private static bool IsQuarterPeriod(EdsInsightsReportOptions options, out int quarter)
    {
        var months = options.Months;
        if (months.SequenceEqual([1, 2, 3])) { quarter = 1; return true; }
        if (months.SequenceEqual([4, 5, 6])) { quarter = 2; return true; }
        if (months.SequenceEqual([7, 8, 9])) { quarter = 3; return true; }
        if (months.SequenceEqual([10, 11, 12])) { quarter = 4; return true; }
        quarter = 0;
        return false;
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

    private static XmlaDebugOptions? AddDebugSuffix(XmlaDebugOptions? debugOptions, string suffix)
    {
        return debugOptions is null
            ? null
            : new XmlaDebugOptions(
                AddFileNameSuffix(debugOptions.RequestOutputPath, suffix),
                AddFileNameSuffix(debugOptions.ResponseOutputPath, suffix));
    }

    private static string? AddFileNameSuffix(string? path, string suffix)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(directory)
            ? $"{fileName}{suffix}{extension}"
            : Path.Combine(directory, $"{fileName}{suffix}{extension}");
    }

    private static string BorderZoneFlag(string borderZone)
    {
        if (string.IsNullOrWhiteSpace(borderZone))
        {
            return "";
        }

        var normalized = NormalizeInvariant(borderZone);
        if (normalized is "SI" or "S" or "1" or "TRUE")
        {
            return "SI";
        }

        if (normalized is "NO" or "N" or "0" or "FALSE")
        {
            return "NO";
        }

        if (normalized.Contains("NO", StringComparison.OrdinalIgnoreCase))
        {
            return "NO";
        }

        if (normalized.Contains("SI", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("FRONTERA", StringComparison.OrdinalIgnoreCase))
        {
            return "SI";
        }

        return "";
    }

    private static string CleanText(string value)
    {
        return value.Replace('\u00A0', ' ').Trim();
    }

    private static bool IsAllMember(string value)
    {
        var normalized = NormalizeInvariant(value);
        return normalized is "ALL" or "(ALL)" or "TODOS";
    }

    private static int? ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static decimal ParseDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static decimal Divide(decimal numerator, decimal denominator)
    {
        return denominator == 0 ? 0 : numerator / denominator;
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    private static string FormatPercentage(decimal value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static string Cell(IReadOnlyList<string> row, int index)
    {
        return index < row.Count ? row[index] : "";
    }

    private static string NormalizeKey(string value)
    {
        return NormalizeInvariant(value).Trim();
    }

    private static string NormalizeInvariant(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    public sealed record CityRow(
        int Rank,
        string DepartmentCode,
        string Department,
        string MunicipalityCode,
        string Municipality,
        decimal Accepted,
        decimal Dispatched);

    public sealed record TopEdsRow(
        int Rank,
        string Code,
        string Name,
        string Municipality,
        string Department,
        string BorderZone,
        decimal Accepted,
        decimal Dispatched);

    public sealed record DominantSupplier(string Provider, string Plant, decimal Volume, decimal TotalVolume);

    public sealed record BorderVolumeRow(
        string BorderFlag,
        int Year,
        string Product,
        string ProductCanonical,
        decimal Accepted,
        decimal Dispatched);

    public sealed record BorderActiveRow(string BorderFlag, decimal ActiveEds);

    private sealed record CityFlagKey(
        int Rank,
        string MunicipalityCode,
        string Municipality,
        string Department,
        string Flag);

    private sealed record ProductZoneKey(string BorderFlag, string Product, string ProductCanonical);
}
