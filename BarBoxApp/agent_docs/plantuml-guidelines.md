# PlantUML Documentation Guidelines

## Common Syntax Errors to Avoid

- **Don't use `!define RECTANGLE class`** - Invalid syntax
- **Don't mix diagram types** - Sequence diagrams can't use activity diagram if/then syntax
- **Always declare all participants** - Reference only declared participants in sequence diagrams
- **Place notes outside component definitions** - Use `note right of` instead of inline notes

## Correct Patterns

### Component Diagrams
```plantuml
@startuml
skinparam componentStyle rectangle
skinparam backgroundColor #FEFEFE

package "Package Name" {
    component "Component1" as C1
    component "Component2" as C2
}

note right of C1 : Description
C1 --> C2 : Relationship
@enduml
```

### Sequence Diagrams
```plantuml
@startuml
participant "Actor1" as A1
participant "Actor2" as A2

A1 -> A2: Message
activate A2

alt Condition
    A2 --> A1: Response1
else Other
    A2 --> A1: Response2
end

deactivate A2
@enduml
```

## Best Practices

- Place all PlantUML diagrams at the bottom of documentation
- Use descriptive section headers before each diagram
- Test diagrams in a PlantUML viewer before committing
- Keep diagrams focused on single concepts
