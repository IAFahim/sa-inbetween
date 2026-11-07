using System.Threading;
using System.Threading.Tasks;
using SA.Inbetween.Soma;

namespace SA.Inbetween.Contract;

public interface IMotionGenerator
{
    ValueTask<Result<Block<GenerationResult>, Fault>> GenerateAsync(ValidRequest request, CancellationToken cancellationToken);
}
