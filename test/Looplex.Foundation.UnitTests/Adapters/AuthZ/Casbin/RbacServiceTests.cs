using System.Reflection;
using System.Security.Claims;

using Casbin;

using Looplex.Foundation.Adapters.AuthZ.Casbin;

using Microsoft.Extensions.Logging;

using NSubstitute;

namespace Looplex.Foundation.UnitTests.Adapters.AuthZ.Casbin;

[TestClass]
public class RbacServiceTests
{
  private IEnforcer _enforcer = null!;
  private ILogger<RbacService> _logger = null!;
  private RbacService _rbacService = null!;
  private ClaimsPrincipal _user = null!;

  [TestInitialize]
  public void SetUp()
  {
    // Set up substitutes
    string testDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                           ?? throw new InvalidOperationException("Could not determine test directory");
    string modelPath = Path.Combine(testDirectory, "model.conf");
    string policyPath = Path.Combine(testDirectory, "policy.csv");

    if (!File.Exists(modelPath) || !File.Exists(policyPath))
    {
      throw new FileNotFoundException("Required Casbin configuration files are missing");
    }

    _enforcer = new Enforcer(modelPath, policyPath);
    _logger = Substitute.For<ILogger<RbacService>>();
    _rbacService = new RbacService(_enforcer, _logger);

    // Mock user context
    _user = Substitute.For<ClaimsPrincipal>();
  }

  [TestMethod]
  public void ThrowIfUnauthorized_UserEmailIsEmpty_ExceptionIsThrown()
  {
    // Arrange
    _user.Claims.Returns(new[] { new Claim(ClaimTypes.Email, ""), new Claim("tenant", "tenant") });

    // Act & Assert
    ArgumentNullException ex =
      Assert.ThrowsException<ArgumentNullException>(() =>
        _rbacService.ThrowIfUnauthorized(_user, "resource", "read"));
    Assert.AreEqual("USER_EMAIL_REQUIRED_FOR_AUTHORIZATION (Parameter 'email')", ex.Message);
  }

  [TestMethod]
  public void ThrowIfUnauthorized_TenantIsNull_ExceptionIsThrown()
  {
    // Arrange
    _user.Claims.Returns(new[] { new Claim(ClaimTypes.Email, "user@email.com") });

    // Act & Assert
    ArgumentNullException ex =
      Assert.ThrowsException<ArgumentNullException>(() =>
        _rbacService.ThrowIfUnauthorized(_user, "resource", "read"));
    Assert.AreEqual("TENANT_REQUIRED_FOR_AUTHORIZATION (Parameter 'tenant')", ex.Message);
  }

  [TestMethod]
  public void ThrowIfUnauthorized_TenantIsEmpty_ExceptionIsThrown()
  {
    // Arrange
    _user.Claims.Returns(new[] { new Claim(ClaimTypes.Email, "user@email.com"), new Claim("tenant", "") });

    // Act & Assert
    ArgumentNullException ex =
      Assert.ThrowsException<ArgumentNullException>(() =>
        _rbacService.ThrowIfUnauthorized(_user, "resource", "read"));
    Assert.AreEqual("TENANT_REQUIRED_FOR_AUTHORIZATION (Parameter 'tenant')", ex.Message);
  }

  [TestMethod]
  [DataRow("read")]
  [DataRow("write")]
  [DataRow("delete")]
  public void ThrowIfUnauthorized_UserHasPermission_NoExceptionThrown(string action)
  {
    // Arrange
    _user.Claims.Returns(new[] { new Claim(ClaimTypes.Email, "bob.rivest@email.com"), new Claim("tenant", "looplex") });

    // Act & Assert (No exception should be thrown)
    _rbacService.ThrowIfUnauthorized(_user, "resource", action);
  }

  [TestMethod]
  [DataRow("execute")]
  public void ThrowIfUnauthorized_UserDoesNotHavePermission_ExceptionIsThrown(string action)
  {
    // Arrange
    _user.Claims.Returns(new[] { new Claim(ClaimTypes.Email, "bob.rivest@email.com"), new Claim("tenant", "looplex") });

    // Act & Assert
    UnauthorizedAccessException ex =
      Assert.ThrowsException<UnauthorizedAccessException>(() =>
        _rbacService.ThrowIfUnauthorized(_user, "resource", action));
    Assert.AreEqual("UNAUTHORIZED_ACCESS", ex.Message);
  }
}