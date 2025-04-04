﻿using NUnit.Framework;
using UITest.Appium;
using UITest.Core;

namespace Microsoft.Maui.TestCases.Tests.Issues;

public class Issue6484 : _IssuesUITest
{
	public Issue6484(TestDevice testDevice) : base(testDevice)
	{
	}

	public override string Issue => "[iOS] Shell - Go back two pages crashes the app with a NullReferenceException";

	[Test]
	[Category(UITestCategories.Shell)]
	public void RemovingIntermediatePagesBreaksShell()
	{
		App.WaitForElementTillPageNavigationSettled("Success");
	}
}