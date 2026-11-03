## MODIFIED Requirements

### Requirement: Consulta de estado

El sistema SHALL exponer `GET /api/sessions/{id}` con estado, timestamps y contadores públicos, sin `OwnerUserId`, `RowVersion` ni `IsDeleted`. Para Participant SHALL ocultar condición e identidad del interlocutor. SHALL informar si quien consulta es Owner, Interlocutor u Observer.

#### Scenario: Consulta de sesión propia
- **WHEN** el propietario consulta su sesión
- **THEN** recibe `200`, su participación Owner y la condición oculta

#### Scenario: Sesión inexistente
- **WHEN** el identificador no existe
- **THEN** recibe `404`

#### Scenario: Interlocutor asignado
- **WHEN** el interlocutor asignado consulta la sesión compartida
- **THEN** recibe su participación Interlocutor sin permisos para cerrarla

#### Scenario: Consulta investigadora
- **WHEN** un Researcher o Administrator consulta una sesión
- **THEN** puede observar su condición y datos experimentales

### Requirement: Aislamiento por propietario

El sistema SHALL responder `404 Not Found` cuando un usuario sin acceso consulta una sesión ajena. El propietario y el interlocutor asignado SHALL poder consultar la conversación compartida; Researcher y Administrator SHALL poder observarla. Sólo el propietario SHALL activar o completar su sesión. La cancelación administrativa SHALL mantener su autorización independiente.

#### Scenario: Consulta de sesión ajena
- **WHEN** un participante sin asignación consulta la sesión de otro usuario
- **THEN** recibe `404`, indistinguible de una sesión inexistente

#### Scenario: Operación sobre sesión ajena
- **WHEN** un interlocutor o un observador intenta activar o completar una sesión de otro propietario
- **THEN** recibe `404` y el estado no cambia

#### Scenario: Rol privilegiado
- **WHEN** un Researcher o Administrator consulta una sesión ajena
- **THEN** recibe `200` con participación Observer
