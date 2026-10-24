# Help Copilot — arquitectura de referencia mínima

Fecha: 2026-10-23.
Estado: insumo para que el agente del producto redacte su ADR con el contexto real del repositorio. No es un ADR adoptado, una especificación cerrada de implementación ni una descripción de funcionalidad ya desplegada.

## 1. Intención del producto

Ofrecer ayuda sencilla y económica dentro de una webapp. El usuario puede recorrer opciones estáticas o escribir una consulta. El servicio identifica documentación pertinente y devuelve un resumen breve con el enlace a la página o sección de ayuda del sitio.

La calidad buscada es orientar al usuario hacia la documentación correcta. No se necesita una IA avanzada, investigación autónoma ni respuestas extensas. Ante información insuficiente, pedir una precisión o mostrar una salida hacia el centro de ayuda es un resultado válido.

Esta referencia es independiente de Kalenda/Pscielo. Recupera principios del flujo madurado —filtro determinístico, evaluación Jev, contexto acotado, selección documental, generación y validación— sin trasladar su plataforma completa.

## 2. Qué queda establecido y qué debe adaptar el agente

Dirección confirmada por el operador:

- Flujo estático de preguntas predeterminadas dentro de la app, sin integración externa para resolverlo.
- Consulta libre a un endpoint conceptual `help-copilot`.
- Corpus formado por documentación de ayuda y sus resúmenes.
- Jev de TypeSafe para evaluaciones semánticas tipadas.
- Dos o tres proveedores generativos configurables, con selección sencilla.
- Defensa frente a jailbreak e inyección, y validación de la salida.
- Respuesta breve y enlace al artículo, con prioridad en simplicidad y bajo costo.

El agente receptor decide cómo encaja esto en el producto: rutas HTTP, clases, tablas, autenticación, permisos, persistencia, modelos, umbrales, límites y política de fallos. Debe leer las instrucciones y decisiones vigentes del repositorio y producir el ADR según sus convenciones. Los nombres de componentes de este documento expresan responsabilidades, no obligan a crear proyectos, servicios o interfaces.

Contexto tecnológico declarado: .NET y webapp; PostgreSQL fue solicitado como referencia. La inspección anterior del checkout encontró SQL Server. Este documento no decide una migración ni una segunda base: el agente debe resolver esa diferencia con el contexto del producto antes de fijar almacenamiento en el ADR.

## 3. Resultado visible

El usuario recibe:

1. Una explicación corta relacionada con su pregunta.
2. El título y enlace de la ayuda que sustenta esa explicación.
3. Si no hay evidencia suficiente, una aclaración o una salida al centro de ayuda.

El enlace sale de los metadatos del corpus, no de una URL inventada por el modelo. Preferir un artículo principal cuando resuelva la consulta; permitir más referencias solo si aportan algo necesario.

El asistente explica cómo usar el producto. El acceso a operaciones, datos de cuentas o acciones sobre el negocio no forma parte de esta arquitectura.

## 4. Flujo de referencia

```text
Webapp: preguntas/opciones estáticas → respuesta local o enlace
                    │
                    └─ Consulta libre
                         ↓
               Validación de acceso y entrada
                         ↓
               Filtro determinístico de seguridad
                         ↓
               Contexto breve, si está habilitado
                         ↓
               Jev: seguridad e interpretación
                         ↓
          Bloquear / aclarar / respuesta local / buscar
                                                    ↓
                                    Recuperación de candidatos
                                                    ↓
                                    Jev: utilidad de la evidencia
                                                    ↓
                                    Selección en código
                                                    ↓
                                    LLM: resumen breve
                                                    ↓
                                    Validación + enlace del catálogo
                                                    ↓
                                    Entrega y actualización de contexto
```

Las ramas que ya tienen una respuesta local no necesitan generación. No hay un agente recorriendo documentos en un bucle: el backend controla cada paso y su presupuesto.

La evaluación previa a la búsqueda y la evaluación documental tienen objetivos diferentes. Puede haber hasta dos evaluaciones Jev en el recorrido completo de referencia, pero no se fija que ambas deban ejecutarse siempre. El agente puede reducir llamadas si conserva las garantías y el comportamiento requerido. No convertir esta secuencia en un motor configurable de workflows por defecto.

## 5. Responsabilidades mínimas

| Pieza | Responsabilidad |
|---|---|
| Webapp | Menú estático, entrada libre, estados de espera/error y presentación segura. |
| Endpoint/orquestación | Validar, limitar, coordinar cancelación y devolver un resultado simple. |
| Seguridad | Reglas determinísticas y evaluación semántica; distinguir rechazo de indisponibilidad. |
| Recuperador | Obtener candidatos autorizados del corpus vigente. |
| Evaluador Jev | Devolver señales tipadas de intención, riesgo o utilidad; no ejecutar acciones. |
| Constructor de contexto | Seleccionar evidencia y preparar instrucciones y datos separados. |
| Selector de proveedores | Usar el proveedor configurado y una contingencia acotada. |
| Validador de salida | Comprobar formato, referencias, longitud y presentación segura. |

Estas piezas pueden vivir en el backend existente. No se requiere otro microservicio, broker, Redis, base vectorial, editor de flows, dashboard de proveedores ni framework de agentes.

## 6. Entrada y seguridad por capas

Antes de cualquier llamada externa, aplicar las políticas de acceso del producto, límites de tamaño y frecuencia, validación de campos y normalización controlada. Las validaciones de la web no sustituyen las del servidor.

La primera capa detecta patrones inequívocos y entradas inválidas mediante código: marcadores de control, intentos explícitos de reemplazar instrucciones, payloads desproporcionados y formatos no admitidos. Las reglas deben tener límites de ejecución y pruebas de falsos positivos. No bloquear una consulta solo por contener una palabra como “jailbreak”.

La segunda capa usa Jev para evaluar intentos de desvío que no se resuelven con sintaxis. Si existe contexto conversacional pertinente, incluirlo de forma acotada para interpretar el mensaje. Jev produce una señal, no una garantía de seguridad.

Separar siempre estos resultados:

- Evaluación válida que permite continuar.
- Evaluación válida que indica bloqueo o incertidumbre.
- Evaluación no disponible, inválida o fallida.

No transformar un timeout o error del proveedor en una probabilidad cero. El ADR debe elegir explícitamente qué hacer si la evaluación no está disponible; una respuesta local de indisponibilidad y enlace general mantiene útil el producto sin generar texto sin evaluar.

Pregunta, memoria y documentación son datos no confiables. No se incorporan como instrucciones privilegiadas. El modelo no recibe credenciales, conexiones a la base ni herramientas para actuar sobre el negocio. El backend fija endpoints y políticas; el navegador no puede elegirlos libremente.

Aplicar los controles de datos sensibles que correspondan al producto antes de enviar texto a terceros. No transportar automáticamente la infraestructura clínica de otra aplicación.

## 7. Papel concreto de Jev / System One

Jev es el modelo de TypeSafe que evalúa preguntas tipadas sobre un estado suministrado por la aplicación. No recupera documentos de la base ni redacta el resumen final. La aplicación recupera y selecciona; el proveedor generativo redacta. [Introducción oficial](https://docs.typesafe.ai/introduction).

El contrato HTTP documentado es `POST https://api.typesafe.ai/v1/systemone`, con autenticación Bearer. El cuerpo incluye `model`, `state` y `questions`; la respuesta devuelve `answers` y datos de uso. Las preguntas usan `instructions` y, cuando corresponde, `criteria`. Verificar el contrato y modelos vigentes al implementar. [Referencia de API](https://docs.typesafe.ai/api/system-one).

Usos propuestos, activando solo lo necesario:

- **Noul:** intento de inyección, consulta dentro del alcance o dependencia del contexto previo.
- **Choice:** consulta nueva, seguimiento o necesidad de aclaración, si esa distinción aporta al chat.
- **Score:** utilidad de un resumen candidato para responder la consulta.

Las instrucciones y rúbricas las define el backend; el mensaje y los documentos van en `state`. Cada pregunta debe evaluar una sola dimensión. Las preguntas de una misma llamada se evalúan independientemente: una no puede utilizar la respuesta de otra dentro de esa llamada.

Para evidencia, una rúbrica simple puede distinguir “no aporta”, “aporta parcialmente” y “permite responder”. Su puntaje no es una probabilidad de verdad. Noul expresa una probabilidad y no tiene un campo separado de confianza. Umbrales, modelos y comportamiento ante ambigüedad se calibran con ejemplos del producto, sin heredar números de otra aplicación. [Score](https://docs.typesafe.ai/primitives/score) · [Noul](https://docs.typesafe.ai/primitives/noul).

Un solo adaptador HTTP puede servir a los distintos usos, con políticas separadas. Validar tipos, rangos, respuestas faltantes y cancelación. No copiar clientes existentes sin contrastar su formato con la API vigente.

## 8. Recuperación y resumen

Cada página o sección debe tener identidad estable, título, resumen útil, ubicación pública y referencia a su versión de origen. Idioma, audiencia y palabras clave se incorporan cuando el producto los necesite. El esquema físico queda a cargo del agente receptor.

Usar primero el mecanismo de búsqueda más sencillo compatible con el repositorio. La búsqueda textual en la base existente puede ser suficiente. No añadir embeddings o infraestructura vectorial sin evidencia de que la recuperación básica no alcanza.

Recuperar un conjunto pequeño y acotado. Jev puede evaluar su pertinencia y el backend ordenar o descartar candidatos. Jev no puede recuperar un artículo que quedó fuera del conjunto inicial. Con un corpus muy pequeño, evaluar sus resúmenes completos puede ser viable dentro del presupuesto; esa elección depende del corpus real.

El LLM recibe la pregunta, la evidencia elegida y una instrucción breve: explicar solo lo respaldado, resumir y abstenerse cuando falten datos. No necesita generar un artículo nuevo ni reproducir toda la documentación. Si ya existe un resumen editorial suficiente, el agente puede contemplar devolverlo directamente con su enlace.

El servidor construye las referencias con metadatos propios. Comprueba formato, tamaño y pertenencia de las fuentes al conjunto autorizado. La existencia de una cita válida no prueba por sí sola que el texto sea correcto: incluir pruebas de fidelidad al artículo.

## 9. Memoria y caché: mantenerlos pequeños y separados

La memoria conversacional sirve para entender “¿y después?”; la caché sirve para evitar trabajo repetido. No son lo mismo.

La referencia admite un contexto breve: tema o artículo activo, pregunta pendiente y algunos turnos recientes. No requiere memoria de largo plazo, extracción de hechos personales ni compactación mediante otra llamada generativa. El agente decide si el producto necesita ese contexto desde la primera versión; si no se conserva, la interfaz y las respuestas deben permitir pedir una pregunta completa.

Cuando haya memoria:

- Vincularla a la identidad y conversación correctas con las reglas del producto.
- Definir expiración, tamaño y tratamiento de concurrencia.
- Conservarla como datos, sin elevar resúmenes a mensajes de sistema.
- Evitar que un saludo sustituya el tema o que un cambio de tema arrastre filtros anteriores.
- Guardar únicamente el resultado válido que corresponda al turno.

No se exige Redis ni almacenamiento nuevo. Reutilizar lo disponible cuando sea suficiente.

Para caché, empezar solo donde aporte: configuración y documentos asociados a una versión del corpus. La caché de respuestas o decisiones semánticas es opcional y puede posponerse. Si se agrega, la clave debe incluir el contexto relevante, alcance de acceso y versiones del corpus/política/modelo; cachear solo por texto confunde preguntas como “sí” entre conversaciones. No reutilizar un resultado entre usuarios o ámbitos sin demostrar que es compartible.

## 10. Selector de proveedores y costo

Mantener dos o tres adaptadores configurables y una selección explícita. No se requiere ranking dinámico, debate, votación, tiers complejos ni llamadas especulativas paralelas.

Normalmente hay una sola generación. Si se habilita fallback, el ADR fija un número pequeño de intentos dentro de un presupuesto total. No reintentar automáticamente en todas las capas. No cambiar de proveedor para eludir un bloqueo de seguridad. Modelos, orden, tiempos y cuotas los define el producto.

Las claves permanecen en el servidor. Cada adaptador traduce el contrato interno al protocolo real del proveedor. La web recibe una respuesta uniforme y no necesita conocer esa elección.

Prioridades económicas:

1. Resolver opciones estáticas localmente.
2. No generar cuando basta una aclaración o un enlace conocido.
3. Enviar pocos resúmenes, no toda la documentación por defecto.
4. Limitar la salida a una explicación breve.
5. Evitar llamadas repetidas y memoria generativa innecesaria.
6. Medir llamadas, latencia y consumo por consulta útil.

No se fijan precios ni presupuestos monetarios en esta referencia.

## 11. Publicación del corpus

El corpus debe corresponder a ayuda publicada, con enlaces válidos. Una actualización incompleta no debe sustituir un índice utilizable: preparar, validar y publicar la nueva versión de forma coherente, conservando la anterior ante fallo. Considerar también la retirada de páginas.

En el contexto aportado para este producto ya se había señalado `web/help/` como corpus y una actualización separada después del deploy exitoso de la UI, sin revertir producción si falla la indexación; el video no era el corpus. El agente receptor debe cotejar esos antecedentes con las decisiones vigentes. Esta referencia no redefine rutas, hosting, jobs ni pipeline de despliegue.

## 12. Contrato externo y observabilidad

El contrato necesita representar, conceptualmente:

- Entrada: pregunta y, si aplica, referencia de conversación o página actual.
- Salida: resultado, resumen breve, título y enlace de ayuda.
- Alternativas: aclaración, ausencia de evidencia, bloqueo o indisponibilidad.

El agente define nombres JSON, ruta, códigos HTTP y errores según las convenciones existentes. Identidad y permisos no se aceptan como afirmaciones libres del cliente. El proveedor, el prompt y los secretos no pertenecen al contrato público.

Registrar resultados, tiempos, número de llamadas, proveedor/modelo y versión de corpus/política cuando sea pertinente. Evitar textos completos y secretos en logs por defecto. La falla de IA no debe inutilizar las opciones estáticas ni los enlaces de ayuda.

## 13. Comprobaciones mínimas para el ADR y la implementación

- Una opción estática funciona sin proveedor externo.
- Una pregunta conocida devuelve un resumen fiel y un enlace existente.
- Una consulta sin respaldo no inventa funciones ni procedimientos.
- Una continuación usa el contexto correcto o pide aclaración.
- Un ataque evidente se corta antes de la llamada generativa.
- Un fallo de Jev se distingue de una evaluación favorable.
- Una instrucción maliciosa dentro de un documento no obtiene capacidades adicionales.
- La salida se presenta sin HTML o enlaces arbitrarios ejecutables.
- Memoria y caché respetan el alcance autorizado.
- La caída de proveedores tiene una salida local comprensible.
- Una actualización fallida conserva el corpus previo.
- No hay reintentos o llamadas sin límite.

Los tests de contrato no prueban la calidad de Jev ni del resumen. Usar un conjunto pequeño de consultas reales, ambiguas, fuera de alcance y adversariales para evaluar esa calidad.

## 14. Encargo para el agente receptor

Tomar este documento como intención arquitectónica, leer el código y la gobernanza del producto y redactar el ADR más pequeño que permita implementarla.

Antes de cerrarlo, resolver con el contexto disponible:

- Integración con backend, frontend, autenticación y permisos existentes.
- Almacenamiento real y ubicación/formato del corpus.
- Necesidad de contexto conversacional y su ciclo de vida.
- Búsqueda inicial y usos de Jev que justifican su costo.
- Proveedores, modelos, límites y contingencias.
- Actualización del corpus y enlaces públicos.
- Validación de salida y criterios medibles de aceptación.

Distinguir requisitos del operador, decisiones ya vigentes, propuestas del ADR y dudas reales. No promover ejemplos o valores de otros proyectos a requisitos de este producto. No importar deuda o infraestructura de otra suite.

Resultado esperado: una ayuda documental breve y económica, cuyo código coordina la búsqueda y las decisiones, Jev aporta evaluaciones acotadas y un LLM redacta solo cuando aporta valor.
