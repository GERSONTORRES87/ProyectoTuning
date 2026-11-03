## ADDED Requirements

### Requirement: Resultado del participante sin revelar condición

El sistema SHALL entregar al Participant únicamente el resultado de su sesión y SHALL mantener oculta la condición y la identidad del interlocutor. Researcher y Administrator SHALL conservar el resultado experimental completo.

#### Scenario: Consulta del participante
- **WHEN** el propietario consulta sus resultados
- **THEN** observa conversación, emociones, avatar y respuestas persistidas sin revelar la condición

#### Scenario: Consulta investigadora
- **WHEN** un Researcher consulta resultados
- **THEN** puede analizar condiciones y respuestas reales con filtros paginados
