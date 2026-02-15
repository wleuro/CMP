echnical Design Document: Controles Cloud Manager (CCM)

Versión: 1.0
Fecha: 16 Feb 2026
Framework: .NET 8 LTS (ASP.NET Core MVC)
Repositorio: GitHub (Private)
1. Resumen Ejecutivo

Controles Cloud Manager (CCM) es la Plataforma de Gestión en la Nube (CMP) propietaria de Controles Empresariales. Su objetivo es centralizar la gestión financiera, operativa y de gobernanza para los clientes CSP, cumpliendo con los requisitos obligatorios del programa Azure Expert MSP (Control 4.17).
2. Alcance del Proyecto (Fase 1: Auditoría)

El sistema debe estar operativo para gestionar un mínimo de 25 clientes activos.

    Módulo Financiero: Visualización de costos, presupuestos y alertas (Control 5.1).

    Módulo de Gobierno: Inventario de activos y cumplimiento de seguridad.

    Módulo de Identidad: Control de acceso basado en roles (RBAC) con MFA (Control 3B.2.3).

3. Arquitectura de la Solución

Se utiliza una arquitectura monolítica modular ("Clean Architecture") para facilitar el despliegue rápido en Azure App Service.
3.1 Diagrama Lógico

    Frontend/Backend: ASP.NET Core MVC (.NET 8).

    Base de Datos: Azure SQL Database.

    Procesamiento en Segundo Plano: IHostedService (dentro de la misma Web App para simplificar costos y despliegue) para la ingesta de datos de APIs.

    Seguridad: Azure Key Vault para gestión de secretos.

3.2 Estructura del Proyecto (Visual Studio Solution)

El nombre raíz del espacio de nombres será Controles.CloudManager.

    Controles.CloudManager.Web (ASP.NET Core MVC):

        Contiene Controladores, Vistas (Razor), y la inyección de dependencias.

        Autenticación: Microsoft.Identity.Web.

    Controles.CloudManager.Core (Class Library):

        Entidades: Tenant, Subscription, CostRecord, AuditLog.

        Interfaces: ICostService, ITenantService.

        Regla: No tiene dependencias externas.

    Controles.CloudManager.Infra (Class Library):

        Data: ApplicationDbContext (Entity Framework Core).

        Integraciones: Implementación de llamadas a Partner Center API y Azure Cost Management API.

4. Estrategia de Datos (Schema SQL)

Diseñado para persistir datos financieros y de auditoría requeridos por el Control 4.17.
SQL

-- Tabla: Clientes (Tenants CSP)
CREATE TABLE Tenants (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    MicrosoftId GUID NOT NULL UNIQUE, -- TenantId de Azure
    Name NVARCHAR(200) NOT NULL,
    Domain NVARCHAR(100),
    IsActive BIT DEFAULT 1,
    OnboardingDate DATETIME DEFAULT GETDATE() -- Vital para probar los 180 días de gestión 
);

-- Tabla: Registros Financieros (Billing)
CREATE TABLE CostRecords (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId INT FOREIGN KEY REFERENCES Tenants(Id),
    SubscriptionId GUID NOT NULL,
    BillingDate DATE NOT NULL,
    Amount DECIMAL(18, 4) NOT NULL,
    Currency CHAR(3) DEFAULT 'USD',
    ServiceCategory NVARCHAR(100) -- "Compute", "Storage", etc.
);

-- Tabla: Auditoría de Acciones (Control 3B.2.4 [cite: 410])
CREATE TABLE AuditLogs (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserEmail NVARCHAR(100), -- Empleado de Controles
    ActionType NVARCHAR(50), -- "VIEW_COST", "EXPORT_REPORT"
    ResourceId NVARCHAR(200),
    Timestamp DATETIME DEFAULT GETDATE()
);

5. Seguridad y Gobernanza (Compliance)

5.1 Identidad (Control 3B.2.5) 

    Autenticación: Exclusivamente vía Microsoft Entra ID (Organizational Account).

    MFA: Obligatorio por política de Acceso Condicional del tenant de Controles.

    Roles (App Roles):

        Admin: Configuración del sistema.

        AccountManager: Ver costos de sus clientes asignados.

        Auditor: Acceso de solo lectura a logs.

5.2 Conexión a Clientes (CSP Model)

Se utilizará el modelo Secure Application Model o Service Principal con GDAP.

    La aplicación no almacena credenciales de clientes.

    Utiliza un App Registration en el tenant de Controles con permisos delegated para acceder a los clientes vía Partner Center.

6. Integración con APIs de Microsoft

Para cumplir con el monitoreo de consumo:

    Partner Center SDK / API: Para obtener la lista de clientes ("Get Customers").

    Azure Cost Management API:

        Endpoint: /subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/query

        Frecuencia: Batch nocturno (Job programado).

7. Ciclo de Desarrollo (SDLC)

Cumplimiento del Control 4.13 (Release Management).

    Control de Versiones: GitHub.

    Estrategia de Ramas:

        main: Producción (Bloqueada).

        develop: Integración.

        feature/nombre-feature: Desarrollo diario.

    Flujo de Trabajo:

        Desarrollador crea rama feature.

        Pull Request (PR) hacia develop.

        Aprobación obligatoria de un par (Code Review).

        Merge.