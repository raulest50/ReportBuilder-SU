# Arquitectura y contrato del informe

## Flujo único

```text
Periodo YYYY-Qn / YYYY-Sn
        |
        +-- Datos Abiertos 339g-zjac ----- volumen despachado
        |
        +-- SICOM OLAP incorporado ------- volumen aceptado
                     |
               CSV crudos locales
                     |
              contratos normalizados
                     |
           validación de meses y productos
                     |
             Python genera 12 SVG
                     |
           Quarto/Typst ensambla PDF/A-2b
                     |
          PDF + métricas + manifiesto/hash
```

La TUI y la CLI llaman `ReportPipeline`; ninguna contiene cálculos de negocio.
`generate` siempre vuelve a consultar las fuentes. `render` no accede a red ni
a SICOM y sirve para iterar sobre diseño con los CSV locales existentes.

## Contratos normalizados

| Archivo | Grano | Medida |
| --- | --- | --- |
| `national-monthly.csv` | año × mes × producto | volumen despachado |
| `geography-departments.csv` | año × producto × departamento | volumen despachado |
| `geography-municipalities.csv` | año × producto × municipio | volumen despachado |
| `mayoristas.csv` | proveedor × año × mes × producto × comprador | volumen aceptado |
| `eds-municipios.csv` | municipio × año × mes × producto | volumen aceptado |
| `eds-activas.csv` | municipio | EDS automotrices activas |

No se permite sumar, sustituir ni presentar como equivalentes las medidas de
Datos Abiertos y SICOM. Los CSV son reproducibles, pero deliberadamente quedan
fuera de Git.

## Especificación de las 12 páginas

1. Portada y marca de borrador cuando el periodo está incompleto.
2. Comportamiento mensual nacional y comparación interanual.
3. Serie histórica del periodo equivalente por producto.
4. Participación y ventas del periodo por mayorista.
5. Participación histórica de mayoristas.
6. Concentración y variación departamental de gasolina corriente.
7. Concentración y variación departamental de ACPM.
8. Comparación municipal destacada.
9. Tablas departamentales.
10. Tablas municipales.
11. EDS activas, volumen aceptado y galones/mes/EDS por municipio.
12. Conclusiones calculadas desde los datos del periodo.

## Reproducibilidad y límites

- La selección temporal se deriva exclusivamente de `PeriodSpec`.
- Cada ejecución escribe sus datos en `data/<periodo>` y sus salidas en
  `build/<periodo>` y `dist/<periodo>`.
- El manifiesto registra meses encontrados, advertencias, contratos de medida,
  versiones, hashes de insumos normalizados y hash del PDF.
- Un faltante parcial de meses produce un borrador visible. La ausencia total
  de una fuente o de un producto requerido detiene el proceso.
- La validación final exige 12 páginas de 13.833 × 8 pulgadas.
