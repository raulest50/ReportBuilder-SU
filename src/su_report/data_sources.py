from __future__ import annotations

import os
from pathlib import Path

import httpx
import pandas as pd

from su_report.models import PeriodSpec, RunPaths
from su_report.settings import Settings


PRODUCTS = (
    "GASOLINA MOTOR CORRIENTE",
    "GASOLINA MOTOR EXTRA",
    "BIODIESEL CON MEZCLA",
)
BUYERS = (
    "COMERCIALIZADOR INDUSTRIAL",
    "ESTACION DE SERVICIO AUTOMOTRIZ",
    "ESTACION DE SERVICIO FLUVIAL",
)


class SourceError(RuntimeError):
    """Una fuente pública no entregó el contrato esperado."""


def _quoted(values: tuple[str, ...] | tuple[int, ...]) -> str:
    return ", ".join(f"'{value}'" for value in values)


def _write_frame(frame: pd.DataFrame, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    frame.to_csv(temporary, index=False)
    temporary.replace(path)


class SocrataExtractor:
    def __init__(self, settings: Settings) -> None:
        source = settings.config["sources"]["datos_abiertos"]
        self.endpoint = f"{source['domain']}/resource/{source['dataset_id']}.json"
        self.timeout = float(source["timeout_seconds"])
        self.app_token = os.environ.get("SOCRATA_APP_TOKEN")

    def _query(self, statement: str) -> pd.DataFrame:
        headers = {"X-App-Token": self.app_token} if self.app_token else None
        try:
            with httpx.Client(timeout=self.timeout, follow_redirects=True, headers=headers) as client:
                response = client.get(self.endpoint, params={"$query": statement})
                response.raise_for_status()
                records = response.json()
        except httpx.HTTPError as error:
            raise SourceError(f"Datos Abiertos no respondió correctamente ({type(error).__name__}).") from error
        if not isinstance(records, list):
            raise SourceError("Datos Abiertos devolvió una respuesta que no es tabular.")
        return pd.DataFrame.from_records(records)

    def fetch(self, period: PeriodSpec, paths: RunPaths, history_years: int) -> dict[str, Path]:
        raw_dir = paths.raw_dir / "datos-abiertos"
        years = period.history_years(history_years)
        products = _quoted(PRODUCTS)
        buyers = _quoted(BUYERS)
        months = _quoted(period.months)
        history = _quoted(years)
        comparison_years = _quoted((period.comparison_year, period.year))

        national_query = f"""
SELECT SUM(volumen_despachado) AS volumen_total,
       anio_despacho, mes_despacho, producto
WHERE subtipo_comprador IN ({buyers})
  AND producto IN ({products})
  AND anio_despacho IN ({history})
GROUP BY anio_despacho, mes_despacho, producto
ORDER BY anio_despacho, mes_despacho, producto
LIMIT 50000
""".strip()
        departments_query = f"""
SELECT SUM(volumen_despachado) AS volumen_total,
       producto, departamento, anio_despacho
WHERE subtipo_comprador IN ({buyers})
  AND producto IN ('GASOLINA MOTOR CORRIENTE', 'BIODIESEL CON MEZCLA')
  AND anio_despacho IN ({comparison_years})
  AND mes_despacho IN ({months})
GROUP BY producto, departamento, anio_despacho
LIMIT 20000
""".strip()
        municipalities_query = f"""
SELECT SUM(volumen_despachado) AS volumen_total,
       producto, municipio, anio_despacho
WHERE subtipo_comprador IN ({buyers})
  AND producto IN ('GASOLINA MOTOR CORRIENTE', 'BIODIESEL CON MEZCLA')
  AND anio_despacho IN ({comparison_years})
  AND mes_despacho IN ({months})
GROUP BY producto, municipio, anio_despacho
LIMIT 30000
""".strip()

        frames = {
            "national": self._query(national_query),
            "departments": self._query(departments_query),
            "municipalities": self._query(municipalities_query),
        }
        expected = {
            "national": {"volumen_total", "anio_despacho", "mes_despacho", "producto"},
            "departments": {"volumen_total", "producto", "departamento", "anio_despacho"},
            "municipalities": {"volumen_total", "producto", "municipio", "anio_despacho"},
        }
        outputs: dict[str, Path] = {}
        for name, frame in frames.items():
            missing = expected[name] - set(frame.columns)
            if missing:
                raise SourceError(f"Faltan columnas en {name}: {', '.join(sorted(missing))}")
            frame["volumen_total"] = pd.to_numeric(frame["volumen_total"], errors="raise")
            output = raw_dir / f"{name}.csv"
            _write_frame(frame, output)
            outputs[name] = output
        return outputs
