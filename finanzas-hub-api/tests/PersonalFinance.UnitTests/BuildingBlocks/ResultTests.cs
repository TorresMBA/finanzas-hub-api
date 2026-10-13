using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.UnitTests.BuildingBlocks;

public class ResultTests
{
    [Fact]
    public void A_value_converts_to_a_successful_result()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void An_error_converts_to_a_failed_result_that_keeps_its_code_and_type()
    {
        Result<int> result = Error.NotFound("ledger.account.not_found", "The account was not found.");

        Assert.True(result.IsFailure);
        Assert.Equal("ledger.account.not_found", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public void Reading_the_value_of_a_failed_result_is_a_programming_error()
    {
        Result<int> result = Error.BusinessRule("rule.broken", "A rule was broken.");

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Reading_the_error_of_a_successful_result_is_a_programming_error()
    {
        var result = Result.Success();

        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void A_validation_error_can_carry_messages_per_field()
    {
        var error = Error.Validation("validation.failed", "Invalid.", new Dictionary<string, string[]> { ["title"] = ["Required."] });

        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(["Required."], error.FieldErrors!["title"]);
    }
}
