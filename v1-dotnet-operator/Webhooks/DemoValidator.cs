// using KubeOps.Abstractions.Webhooks;
// using KubeOps.Abstractions.Webhooks.Admission;
// using PlanescapeStackOperator.Entities;

// namespace PlanescapeStackOperator.Webhooks;

// public class DemoValidator : IValidationWebhook<V1DemoEntity>
// {
//     public AdmissionOperations Operations => AdmissionOperations.Create | AdmissionOperations.Update;

//     public ValidationResult Validate(V1DemoEntity entity)
//         => entity.Spec.Username == "forbiddenUsername"
//             ? ValidationResult.Fail(StatusCodes.Status400BadRequest, "Username is forbidden")
//             : ValidationResult.Success();
// }
