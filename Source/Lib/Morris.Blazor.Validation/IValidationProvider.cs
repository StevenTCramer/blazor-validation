﻿using Microsoft.AspNetCore.Components.Forms;
using System;

namespace Morris.Blazor.Validation
{
	public interface IValidationProvider
	{
		void InitializeEditContext
		(
			EditContext editContext, 
			IServiceProvider serviceProvider, 
			ValidationProperties properties,
			Func<object, object> transformModel = null
		);
	}
}
