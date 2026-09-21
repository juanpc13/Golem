using GolemAPI.Choreography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// The roles a body can play are declared by the operator in the golem's configuration (Juan, 18-sep-2026: "la configuración
// del golem en el compose"): `ROLES=displacer,collision-captor`. What the declaration admits, what it forgives, what it refuses.
[TestClass]
public class CapabilitiesTests
{
    [TestMethod]
    public void TheRolesDeclared_AreTheOnesTheBodyHas_AndNoOther()
    {
        var motorsOnly = Capabilities.Parse("displacer");
        Assert.IsTrue(motorsOnly.Has(Capabilities.Displacer));
        Assert.IsFalse(motorsOnly.Has(Capabilities.CollisionCaptor), "no bumper declared: no bumper");
        Assert.AreEqual("displacer", motorsOnly.ToString());

        var both = Capabilities.Parse("displacer,collision-captor");
        Assert.IsTrue(both.Has(Capabilities.Displacer));
        Assert.IsTrue(both.Has(Capabilities.CollisionCaptor));
        Assert.AreEqual("displacer, collision-captor", both.ToString());
    }

    [TestMethod]
    public void CaseSpacesAndUnderscores_AreForgiven_AndARepeatIsOne()
    {
        var declared = Capabilities.Parse(" Displacer , COLLISION_CAPTOR, displacer ");
        Assert.IsTrue(declared.Has(Capabilities.Displacer));
        Assert.IsTrue(declared.Has(Capabilities.CollisionCaptor));
        Assert.AreEqual("displacer, collision-captor", declared.ToString());
    }

    [TestMethod]
    public void NoDeclaration_KeepsEveryRoleTheSpikeHas()
    {
        foreach (var declared in new[] { null, "", "   " })
        {
            var all = Capabilities.Parse(declared);
            Assert.IsTrue(all.Has(Capabilities.Displacer));
            Assert.IsTrue(all.Has(Capabilities.CollisionCaptor));
        }
    }

    [TestMethod]
    public void ARoleTheGolemCannotPlay_IsRefused_NamingTheOnesItKnows()
    {
        var refusal = Assert.ThrowsException<ArgumentException>(() => Capabilities.Parse("displacer,lidar-scanner"));
        StringAssert.Contains(refusal.Message, "'lidar-scanner'");
        StringAssert.Contains(refusal.Message, "displacer, collision-captor");
    }
}
