# Arquitectura y contrato del informe

## Flujo único

```text
Periodo YYYY-Qn / YYYY-Sn / YYYY-A
        |
        +-- Datos Abiertos 339g-zjac ----- volumen despachado
        |
        +-- SICOM OLAP incorporado ------- volumen aceptado
        |                    +------------ histórico despachado de mayoristas
        |                    +------------ Top 20 EDS por volumen despachado
                     |
               CSV crudos locales
                     |
              contratos normalizados
                     |
           validación de meses y productos
                     |
             Python genera 13 SVG
                     |
           Quarto/Typst ensambla PDF/A-2b
                     |
          PDF + métricas + manifiesto/hash
```

La TUI y la CLI llaman `ReportPipeline`; ninguna contiene cálculos de negocio.
`generate` siempre vuelve a consultar las fuentes. `render` no accede a red ni
a SICOM y sirve para iterar sobre diseño con los CSV locales existentes.

## Composición modular de páginas

`src/su_report/reporting/builder.py` carga una configuración inmutable, prepara
los datos una sola vez y ejecuta un registro explícito de páginas. Cada archivo
de `src/su_report/reporting/pages/page_NN_*.py` implementa exclusivamente su
composición y devuelve la figura en memoria; solo el builder escribe los SVG,
las métricas y la validación histórica.

Las constantes y primitivas visuales compartidas viven en `style.py`, las
tablas genéricas en `tables.py` y las transformaciones de datos en `data.py`.
La plantilla Typst permanece como ensamblador de los SVG terminados.

Para incorporar una página se debe crear su módulo, registrar un `PageSpec` en
el orden requerido, incrementar `report.pages`, añadir su prueba y actualizar
la lista editorial. El builder rechaza números faltantes, duplicados o una
cantidad distinta de la configurada.

## Contratos normalizados

| Archivo | Grano | Medida |
| --- | --- | --- |
| `national-monthly.csv` | año × mes × producto | volumen despachado |
| `geography-departments.csv` | año × producto × departamento | volumen despachado |
| `geography-municipalities.csv` | año × producto × municipio | volumen despachado |
| `mayoristas.csv` | proveedor × año × mes × producto × comprador | volumen aceptado |
| `mayoristas-historico-mensual.csv` | mes × proveedor | volumen despachado y participación mensual |
| `eds-municipios.csv` | municipio × año × mes × producto | volumen aceptado |
| `eds-activas.csv` | municipio | EDS automotrices activas |
| `eds-top-volumen.csv` | rango × EDS automotriz | volumen despachado y participación nacional |
| `zfd-resumen.csv` | zona frontera × periodo | volumen despachado, variación, EDS activas e intensidad |
| `zfd-productos.csv` | zona frontera × producto | volumen despachado y mezcla de producto |
| `zfd-municipios.csv` | rango × municipio ZFD | volumen despachado y participación fronteriza |

No se permite sumar, sustituir ni presentar como equivalentes las medidas de
Datos Abiertos y SICOM. Los CSV son reproducibles, pero deliberadamente quedan
fuera de Git.

## Especificación de las 14 páginas

1. Portada y marca de borrador cuando el periodo está incompleto.
2. Comportamiento mensual nacional y comparación interanual.
3. Serie histórica del periodo equivalente por producto.
4. Participación y ventas del periodo por mayorista.
5. Participación histórica mensual de mayoristas mediante áreas apiladas; volumen despachado a EDS automotrices y fluviales.
6. Concentración y variación departamental de gasolina corriente.
7. Concentración y variación departamental de ACPM.
8. Comparación municipal destacada.
9. Tablas departamentales.
10. Tablas municipales.
11. EDS activas, volumen aceptado y galones/mes/EDS por municipio.
12. Top 20 EDS automotrices por volumen despachado y participación nacional.
13. Comparación de zonas de frontera frente al resto del país con volumen despachado, EDS activas, top municipal y mezcla de producto.
14. Conclusiones calculadas desde los datos del periodo.

## Reproducibilidad y límites

- La selección temporal se deriva exclusivamente de `PeriodSpec`.
- Cada ejecución escribe sus datos en `data/<periodo>` y sus salidas en
  `build/<periodo>` y `dist/<periodo>`.
- El histórico de mayoristas comienza en `2011-01`, termina en el último mes
  del periodo solicitado y conserva partes anuales reanudables dentro del snapshot.
- El manifiesto registra meses encontrados, advertencias, contratos de medida,
  versiones, hashes de insumos normalizados y hash del PDF.
- Un faltante parcial de meses en periodos trimestrales o semestrales produce
  un borrador visible. Un informe anual exige los doce meses completos.
- La ausencia total de una fuente o de un producto requerido detiene el proceso.
- La validación final exige 14 páginas de 13.833 × 8 pulgadas.
