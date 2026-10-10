namespace ThreeWayMatch.Domain.Rules;

/// <summary>
/// One rule from the discrepancy catalogue. Pure: it reads the context and returns findings, with no database call
/// and no dependence on any other rule. A new rule is a new class added to <see cref="DefaultRules"/>.
/// </summary>
public interface IDiscrepancyRule
{
    IEnumerable<Finding> Evaluate(RuleContext context);
}
