Technical Design Document: CMP by COEM

Versión: 1.0
Fecha: 16 Feb 2026
Framework: .NET 8 LTS (ASP.NET Core MVC)
Repositorio: cmp-by-coem
1. Resumen Ejecutivo

CMP by COEM es la Plataforma de Gestión en la Nube automatizada de Controles Empresariales. Su propósito es centralizar la gobernanza, gestión financiera y operaciones de los clientes bajo el modelo CSP, asegurando el cumplimiento de los estándares de auditoría Azure Expert MSP (Control 4.17).

2. Alcance y Alineación con Auditoría (Checklist V2.6)

El desarrollo de esta plataforma es la evidencia primaria para los siguientes controles obligatorios:

    Control 4.17 (Automated CMP):

        Requisito: Interfaz de gestión para 25+ clientes activos.

        Implementación: Módulo "Service Request Management" y "Consumption Monitoring".

    Control 5.1 (Cloud SLA & SLO):

        Requisito: Monitoreo de tiempos de respuesta y cumplimiento de niveles de servicio.

        Implementación: Dashboard de SLAs financieros y operativos.

    Control 3B.2.5 (Credential Management):

        Requisito: Gestión segura de credenciales y RBAC.

        Implementación: Integración con Microsoft Entra ID y Azure Key Vault.
		
3. Arquitectura de Solución

La solución sigue una arquitectura limpia ("Clean Architecture") monolítica modular, optimizada para despliegue en Azure App Service.
3.1 Stack Tecnológico

    Frontend/Backend: ASP.NET Core MVC (.NET 8.0 LTS).

    Base de Datos: Azure SQL Database.

    Identidad: Microsoft Entra ID (OpenID Connect).

    Procesamiento en Background: IHostedService (dentro de la Web App) para sincronización con APIs.

3.2 Estructura del Proyecto (Solution Explorer)

El namespace raíz será Coem.Cmp.

    Coem.Cmp.Web (ASP.NET Core MVC):

        Controladores y Vistas (Razor) para la interacción del usuario (TAMs/Preventas).

        Configuración de Inyección de Dependencias (DI).

    Coem.Cmp.Core (Class Library):

        Entidades: Tenant, Subscription, BillingRecord.

        Interfaces: IPartnerCenterService, ICostService.

        Regla: No tiene dependencias de infraestructura.

    Coem.Cmp.Infra (Class Library):

        Data: ApplicationDbContext (Entity Framework Core).

        Integraciones: Implementación de clientes HTTP para Partner Center API y Azure Cost Management API.
		
4. Estrategia de Datos (Schema SQL)

Esquema relacional diseñado para persistir la evidencia requerida por el Control 4.17.

-- Tabla: Clientes CSP (Evidence: 25 Active Customers) 
CREATE TABLE Tenants (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    MicrosoftTenantId GUID NOT NULL UNIQUE,
    Name NVARCHAR(200) NOT NULL,
    DefaultDomain NVARCHAR(100),
    OnboardingDate DATETIME DEFAULT GETDATE(), -- Valida los 180 días de gestión [cite: 311]
    IsActive BIT DEFAULT 1
);

-- Tabla: Consumo Financiero (Evidence: Consumption Monitoring) [cite: 446]
CREATE TABLE CostRecords (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId INT FOREIGN KEY REFERENCES Tenants(Id),
    SubscriptionId GUID NOT NULL,
    BillingPeriodStart DATE,
    BillingPeriodEnd DATE,
    PreTaxCost DECIMAL(18, 4),
    Currency CHAR(3) DEFAULT 'USD',
    ServiceCategory NVARCHAR(100) -- e.g. "Virtual Machines", "Storage"
);

-- Tabla: Auditoría (Evidence: Access Management & Audit Logs) [cite: 446]
CREATE TABLE AuditLogs (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserEmail NVARCHAR(100), -- Usuario de COEM
    Action NVARCHAR(50), -- "VIEW_COST", "UPDATE_BUDGET"
    Timestamp DATETIME DEFAULT GETDATE(),
    IpAddress NVARCHAR(50)
);

5. Seguridad y Gobernanza
5.1 Secure App Model (GDAP)

Para cumplir con el acceso seguro a los entornos de los clientes:

    App Registration: La aplicación utiliza una Identidad de Servicio (Service Principal).

    GDAP: Se configuran relaciones de Granular Delegated Admin Privileges con los clientes, asignando el rol de Global Reader o Directory Reader a la App.

    Secretos: El Client Secret se almacena exclusivamente en Azure Key Vault y se accede vía Managed Identity en producción.

6. Integración de APIs (Tubería de Datos)

    Partner Center SDK/API: Sincronización diaria de clientes nuevos y estado de suscripciones.

    Azure Cost Management API: Extracción diaria de costos amortizados y reales para detectar desviaciones presupuestales (Control 5.1 ).

7. Plan de Desarrollo (Fase 1: MVP Auditoría)

    Semana 1: Configuración de Repositorio, CI/CD Básico, Autenticación Entra ID y Listado de Clientes (Conexión Partner Center).

    Semana 2: Ingesta de Datos Financieros (Azure Cost Management) y Dashboard Básico de Presupuestos.
	
---------------------------------------------------------------------------------------------------------------

FASE 1: El Core del CMP (Cumplimiento del Control 4.17 - Category 0)

El Control 4.17  es una "Hard Fail" (Categoría 0). Si fallas aquí, se acabó la auditoría. Requiere 25 clientes activos en la plataforma.

Arquitectura:

    Backend: .NET 8 (Clean Architecture).

    Frontend: Blazor WebAssembly o React (rápido, moderno).

    Identity: Microsoft Entra ID (Multitenant). Crucial: Debes implementar GDAP aquí.

    Database: Azure SQL (Auditoría habilitada).

Funcionalidades Obligatorias (Sprint 1-3):

    Service Request Management (Catálogo de Servicios):

        Requisito: Interfaz donde el cliente solicita servicios.

        Tu Tool: Un "Marketplace interno" donde el cliente pide "Nueva VM", "Licencia E3", o "Soporte".

        El Truco Zenith: No necesitas automatizar el provisioning al 100% el día 1. Lo que necesitas es que el ticket se genere en la herramienta. Eso es la evidencia.

    Consumption Monitoring (La vista financiera):

        Requisito: Monitoreo de consumo a nivel recurso/suscripción.

        Tu Tool: Ingesta de datos de Azure Cost Management API.

        Diferenciador: Agrega la lógica de "Presupuesto vs. Real" y alertas. Esto cubre también el Control 5.1 (Cloud SLA/SLO)  al mostrar tiempos de respuesta y cumplimiento financiero.

    Access Management (RBAC):

        Requisito: Control de quién puede hacer qué (cliente y staff).

        Tu Tool: Mapea roles de tu herramienta a grupos de seguridad de Entra ID. Nunca crees usuarios locales en la base de datos.

FASE 2: La "Grasa" hecha Músculo (Gobernanza y Alertas)

Aquí es donde matas la ineficiencia operativa.

Funcionalidades (Sprint 4-6):

    El "Zombie Killer" (Optimización de Recursos):

        Requisito: Control 4.17 pide explícitamente "Resource utilization optimization".

        Tu Tool:

            Azure: Listar VMs con CPU < 5% por 7 días. (Recomendar Resize/Shutdown).

            M365: Listar usuarios con licencia asignada sin login en 30 días.

        Valor: Esto es lo que venderán tus TAMs. "Te ahorramos dinero automáticamente".

    Integration con Lighthouse (Control 4.18 - Monitoring at Scale):

        Requisito: Monitorear múltiples clientes a escala.

        Tu Tool: No reinventes la rueda. Usa Azure Lighthouse. Tu herramienta debe leer los logs de Azure Monitor que Lighthouse centraliza y mostrarlos en un dashboard unificado: "Estado de Salud de los 25 Clientes Principales".

        Evidence: Cuando el auditor pida ver el Control 4.18, les muestras esta pantalla.

FASE 3: El Escudo de Auditoría (Security & Compliance)

El checklist V2.6 ha subido la barra en seguridad (100 puntos mínimos en la sección 2.1 Security).

Funcionalidades (Sprint 7-9):

    Security Scorecard (Control 3A.3):

        Requisito: Herramientas de gobernanza y seguridad para 2 proyectos.

        Tu Tool: Conéctate a la API de Microsoft Defender for Cloud. Muestra el "Secure Score" de cada cliente en tu dashboard.

        Alerta Zenith: Si el Score baja de 60%, tu herramienta debe abrir un ticket automático en tu ITSM (ServiceNow/Jira/DevOps). Eso demuestra el Control 4.8 (Event Management).

    Gestión de Logs de Auditoría (Control 3B.2.4):

        Requisito: Revisión de políticas de seguridad.

        Tu Tool: Debe tener un "Log inmutable". ¿Quién aprobó el cambio de tamaño de la VM? ¿Quién asignó la licencia E5? Todo queda registrado. Esto es oro puro para el auditor.

El Plan de Ejecución "Sin Excusas" (Timeline 2026)

Will, si quieres esto listo para cuando las "otras áreas" despierten, este es tu cronograma:

    Mes 1: El Esqueleto (MVP).

        Arquitectura desplegada en Azure (App Service + SQL).

        Conexión a Partner Center (autenticación segura).

        Dashboard simple de "Lista de Clientes" (debe listar los 25 requeridos para el Control 4.17 ).

    Mes 2: El Dinero (Costos).

        Módulo de Azure Cost Management funcionando.

        Módulo de Licenciamiento M365 (conteo básico).

    Mes 3: La Optimización (El Valor).

        Algoritmos de detección de desperdicio (Zombies M365 + VMs ociosas).

        Integración básica con Lighthouse (lectura de alertas).

    Mes 4: El Simulacro.

        Usas la herramienta para gestionar a esos 25 clientes reales durante 30 días.

        Recopilas los logs generados como evidencia.

Tu Ventaja Competitiva: El Centro de Excelencia (CoE)

El checklist pide un Partner Cloud Center of Excellence (Control 1.8).
Si tu herramienta centraliza las mejores prácticas, los templates de ARM/Bicep (Control 3A.5 ) y la gobernanza, tú no solo lideras tu área, lideras la estrategia del CoE de facto.

--------------------------------------------------------------------------------------------------

Objetivo del Sprint (Semana 1): "Show me the Money & The Structure".

Entregable: Una aplicación Web (Blazor/.NET) que autentica empleados de Controles, lista los 25 clientes requeridos para la auditoría, y muestra el consumo actual vs. presupuesto de Azure.
Épica 1: El Esqueleto (Seguridad y Gobernanza)

Justificación Audit: Control 3B.2.5 (Access Control) y 4.17 (Automated CMP - Access Management).

Épica 2: El Dinero (Gestión de Costos y Facturación)

Justificación Audit: Control 4.17 (Consumption monitoring & metering) y 5.1 (SLA/SLO)

 Gemini said

Will, me gusta esa agresividad. Si queremos el Esqueleto y el Dinero para esta misma semana, entramos en modo "War Room". Olvida las interfaces bonitas; esta semana se trata de Tubería de Datos y Precisión Financiera.

Para lograr esto en 5 días, la documentación debe ser tu biblia. Aquí tienes la confirmación técnica y el documento maestro de Historias de Usuario alineadas con la auditoría para que tu equipo ejecute ya.
1. Confirmación Técnica: El Acceso a Partner Center

Sí, es mediante un App Registration, pero no es una app estándar. Para un CMP que funciona en background (sin un usuario logueado todo el tiempo), necesitas implementar el Secure Application Model (CPV) o usar el flujo de Service Principal que Microsoft está empujando ahora para GDAP.

La Arquitectura de Conexión (Semana 1):

    App Registration (Entra ID): Creas la app en tu tenant de partner.

    Permisos de API:

        Microsoft Partner Center API: user_impersonation (Delegated) y permisos de Application si usas el nuevo modelo.

        Microsoft Graph API: Directory.Read.All, Reports.Read.All (para uso de licencias).

        Azure Management API: user_impersonation.

    La Clave (GDAP): Tu App Registration debe ser agregada a un grupo de seguridad que tenga asignado el rol de Global Reader (para auditoría) o Admin Agent (para gestión) en los clientes a través de relaciones GDAP activas.

        Advertencia: Sin GDAP configurado en el Partner Center, la API te rechazará las llamadas a los tenants de los clientes.

2. Documento Maestro: Zenith CMP - Backlog de Aceleración

Copia y pega esto en tu DevOps/Jira. Está mapeado directamente al PDF de auditoría V2.6 para matar dos pájaros de un tiro.
Objetivo del Sprint (Semana 1): "Show me the Money & The Structure".

Entregable: Una aplicación Web (Blazor/.NET) que autentica empleados de Controles, lista los 25 clientes requeridos para la auditoría, y muestra el consumo actual vs. presupuesto de Azure.
Épica 1: El Esqueleto (Seguridad y Gobernanza)

Justificación Audit: Control 3B.2.5 (Access Control) y 4.17 (Automated CMP - Access Management).

ID,Historia de Usuario,Criterios de Aceptación (Definition of Done),Mapping Auditoría (V2.6)
SYS-01,"Autenticación Multitenant: Como administrador de Controles, quiero loguearme con mi cuenta corporativa (MFA) para acceder a la plataforma de forma segura.",1. Login exitoso con Entra ID.2. MFA forzado por política condicional.3. El token de sesión expira a los 60 min.,3B.2.3 (MFA Required) 
SYS-02,"Sincronización de Clientes: Como sistema, quiero consultar la API de Partner Center para obtener la lista de clientes activos y sus IDs de Tenant.",1. Job nocturno (Azure Function) trae lista de clientes.2. Se identifican clientes con GDAP activo/inactivo.3. Se guardan en BD local (SQL) para caché.,4.17 (25 Active Customers) 
SYS-03,"Role-Based Access (RBAC): Como líder de área, quiero asignar roles (Admin, Lector, Finanzas) a mi equipo para limitar quién ve qué.",1. Roles definidos en código o BD.2. Un preventa solo ve sus clientes asignados.3. Log de auditoría de quién accedió a qué cliente.,3B.2.5 (Account Credential Mgmt) 

Épica 2: El Dinero (Gestión de Costos y Facturación)

Justificación Audit: Control 4.17 (Consumption monitoring & metering) y 5.1 (SLA/SLO).

ID,Historia de Usuario,Criterios de Aceptación (Definition of Done),Mapping Auditoría (V2.6)
FIN-01,"Ingesta de Costos Azure: Como TAM, quiero ver el consumo acumulado del mes actual (Month-to-Date) de mis clientes para detectar desviaciones.",1. Conexión a Azure Cost Management API.2. Visualización de costo total vs. mes anterior.3. Datos actualizados con máximo 24h de latencia.,4.17 (Consumption Monitoring) 
FIN-02,"Alertas de Presupuesto: Como sistema, quiero enviar una alerta si un cliente supera el 80% de su consumo promedio histórico antes del día 20 del mes.",1. Cálculo de proyección de cierre de mes.2. Alerta visual en Dashboard (Rojo/Amarillo).3. Registro de la alerta en base de datos.,5.1 (Cloud SLA/SLO tracking) 
FIN-03,"Desglose de Recursos: Como Preventa, quiero ver el Top 5 de recursos más costosos de un cliente para sugerir optimizaciones.",1. Drill-down de Costo Total -> Resource Group -> Recurso.2. Gráfico de torta o barra simple.,4.17 (Resource utilization) 

Épica 3: La Eficiencia (Optimización - Fase siguiente)

Justificación Audit: Control 4.17 (Resource utilization optimization). Preparar el terreno para la semana 2.

ID,Historia de Usuario,Criterios de Aceptación (Definition of Done),Mapping Auditoría (V2.6)
OPT-01,"Identificación de Reservas (RI): Como consultor, quiero saber qué VMs no tienen Reservas asignadas para ofrecer ahorro al cliente.",1. Listar VMs corriendo 24/7 (730 horas/mes).2. Cruzar con inventario de RIs activas.3. Calcular ahorro potencial ($).,4.17 (Optimization) 