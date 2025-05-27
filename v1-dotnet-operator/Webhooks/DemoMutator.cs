// using KubeOps.Abstractions.Webhooks;
// using KubeOps.Abstractions.Webhooks.Admission;
// using PlanescapeStackOperator.Entities;

// namespace PlanescapeStackOperator.Webhooks;

// public class DemoMutator : IMutationWebhook<V1DemoEntity>
// {
//     public AdmissionOperations Operations => AdmissionOperations.Create | AdmissionOperations.Update;

//     public MutationResult Mutate(V1DemoEntity entity)
//     {
//         entity.Spec.Username = "not foobar";
//         return MutationResult.Modified(entity);
//     }
// }
