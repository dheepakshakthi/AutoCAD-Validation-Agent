"""
Rule Engine - Evaluates geometry against loaded rules.
MVP: Rules are defined in code. Future: Load from JSON/database.
"""

import uuid
from typing import Callable
from models import Entity, Violation, RuleDefinition, RuleListResponse


class Rule:
    """A validation rule with its evaluation function"""
    
    def __init__(
        self,
        id: str,
        name: str,
        category: str,
        severity: str,
        description: str,
        entity_types: list[str],
        condition: dict,
        evaluate_fn: Callable[[Entity], tuple[bool, str]],
    ):
        self.id = id
        self.name = name
        self.category = category
        self.severity = severity
        self.description = description
        self.entity_types = entity_types
        self.condition = condition
        self.evaluate_fn = evaluate_fn
    
    def applies_to(self, entity: Entity) -> bool:
        """Check if this rule applies to the given entity type"""
        return entity.type in self.entity_types or "*" in self.entity_types
    
    def evaluate(self, entity: Entity) -> tuple[bool, str]:
        """
        Evaluate the rule against an entity.
        Returns (passed, message).
        """
        return self.evaluate_fn(entity)
    
    def to_definition(self) -> RuleDefinition:
        return RuleDefinition(
            id=self.id,
            name=self.name,
            category=self.category,
            severity=self.severity,
            description=self.description,
            entity_types=self.entity_types,
            condition=self.condition,
        )


class RuleEngine:
    """Engine that loads and evaluates validation rules"""
    
    def __init__(self):
        self.rules: list[Rule] = []
        self._load_default_rules()
    
    def _load_default_rules(self):
        """Load MVP rules - these would come from JSON/DB in production"""
        
        # Dimension Rules
        self.rules.append(Rule(
            id="dim_min_radius",
            name="Minimum Circle Radius",
            category="Dimensions",
            severity="high",
            description="Circle radius must be at least 5mm for manufacturability",
            entity_types=["Circle"],
            condition={"property": "radius", "operator": ">=", "value": 5.0},
            evaluate_fn=lambda e: (
                (e.properties.radius or 0) >= 5.0,
                f"Circle radius {e.properties.radius}mm is below minimum 5mm"
            ),
        ))
        
        self.rules.append(Rule(
            id="dim_max_radius",
            name="Maximum Circle Radius",
            category="Dimensions",
            severity="medium",
            description="Circle radius should not exceed 500mm",
            entity_types=["Circle"],
            condition={"property": "radius", "operator": "<=", "value": 500.0},
            evaluate_fn=lambda e: (
                (e.properties.radius or 0) <= 500.0,
                f"Circle radius {e.properties.radius}mm exceeds maximum 500mm"
            ),
        ))
        
        self.rules.append(Rule(
            id="dim_line_length",
            name="Maximum Line Length",
            category="Dimensions",
            severity="medium",
            description="Line length should not exceed 1000mm",
            entity_types=["Line"],
            condition={"property": "length", "operator": "<=", "value": 1000.0},
            evaluate_fn=lambda e: (
                (e.properties.length or 0) <= 1000.0,
                f"Line length {e.properties.length}mm exceeds maximum 1000mm"
            ),
        ))
        
        # Layer Rules
        self.rules.append(Rule(
            id="layer_valid",
            name="Valid Layer Assignment",
            category="Layers",
            severity="medium",
            description="Objects must be on recognized layers (not layer 0 for production)",
            entity_types=["*"],
            condition={"property": "layer", "operator": "not_in", "value": ["0"]},
            evaluate_fn=lambda e: (
                e.layer != "0",
                f"Entity on default layer '0' - assign to proper layer"
            ),
        ))
        
        # Text Rules
        self.rules.append(Rule(
            id="text_min_height",
            name="Minimum Text Height",
            category="Text",
            severity="low",
            description="Text height must be at least 2.5mm for readability",
            entity_types=["Text", "MText"],
            condition={"property": "text_height", "operator": ">=", "value": 2.5},
            evaluate_fn=lambda e: (
                (e.properties.text_height or 0) >= 2.5,
                f"Text height {e.properties.text_height}mm is below minimum 2.5mm"
            ),
        ))
        
        # Geometry Rules
        self.rules.append(Rule(
            id="geom_arc_angle",
            name="Arc Angle Range",
            category="Geometry",
            severity="low",
            description="Arc angles should be meaningful (> 5 degrees)",
            entity_types=["Arc"],
            condition={"property": "arc_angle", "operator": ">=", "value": 5.0},
            evaluate_fn=lambda e: self._check_arc_angle(e),
        ))
    
    def _check_arc_angle(self, entity: Entity) -> tuple[bool, str]:
        """Check if arc has meaningful angle"""
        start = entity.properties.start_angle or 0
        end = entity.properties.end_angle or 0
        angle = abs(end - start)
        if angle < 5.0:
            return False, f"Arc angle {angle}° is too small (minimum 5°)"
        return True, ""
    
    def get_categories(self) -> list[str]:
        """Get unique categories"""
        return list(set(r.category for r in self.rules))
    
    def get_rules_summary(self) -> RuleListResponse:
        """Get all rules for listing"""
        return RuleListResponse(
            categories=self.get_categories(),
            rules=[r.to_definition() for r in self.rules],
            total_count=len(self.rules),
        )
    
    def validate(self, entities: list[Entity]) -> list[Violation]:
        """
        Validate all entities against all applicable rules.
        Returns list of violations.
        """
        violations: list[Violation] = []
        
        for entity in entities:
            for rule in self.rules:
                if not rule.applies_to(entity):
                    continue
                
                passed, message = rule.evaluate(entity)
                if not passed:
                    violations.append(Violation(
                        id=f"v_{uuid.uuid4().hex[:8]}",
                        rule_id=rule.id,
                        category=rule.category,
                        severity=rule.severity,
                        message=message,
                        entity_ref=entity.handle,
                    ))
        
        return violations
