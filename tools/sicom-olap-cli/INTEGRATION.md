# Integración del motor SICOM OLAP

Esta carpeta contiene una copia autocontenida de las fuentes operativas de
`Cli-Tools/win-sbi-olap-cli` existentes al crear `ReportBuilder-SU` el
2026-07-19. No existe una dependencia por ruta hacia el workspace de origen.

Se incorporaron literalmente las rutinas C# de:

- conexión HTTP/XMLA sobre `msmdpump.dll`;
- resolución segura de `.env` y variables de entorno;
- diagnóstico, catálogos, cubos, perfiles y ejecución MDX;
- reportes `mayoristas`, `eds_municipios`, `eds_insights` y
  `eds_percentiles`;
- pruebas unitarias y consultas MDX de referencia.

Los únicos ajustes de integración iniciales fueron nombres descriptivos,
separadores portables en archivos de proyecto y expectativas de rutas
multiplataforma en las pruebas. Los filtros, medidas y consultas de negocio no
se reescribieron.

El orquestador ejecuta el proyecto
`src/WinSbi.Olap.Cli/WinSbi.Olap.Cli.csproj`. Si existe un binario Release lo
usa; de lo contrario invoca `dotnet run`. La aplicación nunca pasa usuario ni
contraseña como argumentos: el proceso C# hereda las variables cargadas desde
el `.env` local del repositorio.

Al actualizar esta copia desde el proyecto de origen, compare primero las
fuentes y aplique deliberadamente los cambios. No copie `bin/`, `obj/`,
`reports/`, `.env` ni resultados generados.
