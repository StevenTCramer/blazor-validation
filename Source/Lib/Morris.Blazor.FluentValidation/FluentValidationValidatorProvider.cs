﻿using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Results;
using Microsoft.AspNetCore.Components.Forms;
using Morris.Blazor.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Morris.Blazor.FluentValidation
{
	public class FluentValidationValidatorProvider : IValidationProvider
	{
		private Func<object, object> OnTransformModel { get; set; } = null;

		public void InitializeEditContext
		(
			EditContext editContext,
			IServiceProvider serviceProvider,
			ValidationProperties properties,
			Func<object, object> transformModel = null
		)
		{
			if (editContext == null)
				throw new ArgumentNullException(nameof(editContext));
			if (serviceProvider == null)
				throw new ArgumentNullException(nameof(serviceProvider));
			properties ??= ValidationProperties.Set;
			OnTransformModel = transformModel;

			var messages = new ValidationMessageStore(editContext);
			editContext.OnValidationRequested +=
				(sender, eventArgs) =>
				{
					_ = ValidateModel((EditContext)sender, messages, serviceProvider, properties);
				};

			editContext.OnFieldChanged +=
				(sender, eventArgs) =>
				{
					_ = ValidateField(editContext, messages, eventArgs.FieldIdentifier, serviceProvider);
				};
		}

		private async Task ValidateModel(
			EditContext editContext,
			ValidationMessageStore messages,
			IServiceProvider serviceProvider,
			ValidationProperties properties)
		{
			if (editContext == null)
				throw new ArgumentNullException(nameof(editContext));
			if (messages == null)
				throw new ArgumentNullException(nameof(messages));
			if (serviceProvider == null)
				throw new ArgumentNullException(nameof(serviceProvider));
			if (editContext.Model == null)
				throw new NullReferenceException($"{nameof(editContext)}.{nameof(editContext.Model)}");

			messages.Clear();
			editContext.NotifyValidationStateChanged();

			object transformedModel = GetTransformedModel(editContext.Model);

			IEnumerable<IValidator> validators = GetValidatorsForObject(transformedModel, serviceProvider);

			var validationContext = new ValidationContext<object>(transformedModel);

			var validationResults = new List<ValidationResult>();
			foreach (IValidator validator in validators)
			{
				var validationResult = await validator.ValidateAsync(validationContext);
				validationResults.Add(validationResult);
			}

			IEnumerable<ValidationFailure> validationFailures = validationResults.SelectMany(x => x.Errors);
			foreach (var validationError in validationFailures)
			{
				GetParentObjectAndPropertyName(transformedModel, validationError.PropertyName, out object parentObject, out string propertyName);
				if (parentObject != null)
					messages.Add(new FieldIdentifier(parentObject, propertyName), validationError.ErrorMessage);
			}

			editContext.NotifyValidationStateChanged();
		}
		private object GetTransformedModel(object model)
		{
			return OnTransformModel is not null ? OnTransformModel(model) : model;
		}

		private void GetParentObjectAndPropertyName(
			object model,
			string propertyPath,
			out object parentObject,
			out string propertyName)
		{
			if (model == null)
				throw new ArgumentNullException(nameof(model));

			var propertyPathParts = new Queue<string>(propertyPath.Split('.'));
			Type modelType = model.GetType();
			while (propertyPathParts.Count > 1)
			{
				string name = propertyPathParts.Dequeue();

				string propertyIndexString = null;
				int bracketIndex = name.IndexOf('[');
				if (bracketIndex > 0)
				{
					propertyIndexString = name.Substring(bracketIndex + 1, name.Length - bracketIndex - 2);
					name = name.Remove(bracketIndex);
				}

				PropertyInfo propertyInfo = modelType.GetProperty(name);
				model = model == null
					? null
					: propertyInfo.GetValue(model, null);

				if (propertyIndexString == null)
					modelType = propertyInfo.PropertyType;
				else
				{
					List<object> collection = ((IEnumerable<object>)model).ToList();
					int propertyIndex = int.Parse(propertyIndexString);
					model = collection[propertyIndex];
				}
			}

			parentObject = model;
			propertyName = propertyPathParts.Dequeue();
		}

		private async Task ValidateField
		(
			EditContext editContext,
			ValidationMessageStore messages,
			FieldIdentifier fieldIdentifier,
			IServiceProvider serviceProvider
		)
		{
			if (editContext == null)
				throw new ArgumentNullException(nameof(editContext));
			if (messages == null)
				throw new ArgumentNullException(nameof(messages));
			if (serviceProvider == null)
				throw new ArgumentNullException(nameof(serviceProvider));
			if (editContext.Model == null)
				throw new NullReferenceException($"{nameof(editContext)}.{nameof(editContext.Model)}");

			object transformedModel = GetTransformedModel(editContext.Model);
			var propertiesToValidate = new string[] { fieldIdentifier.FieldName };
			var fluentValidationContext =
				new ValidationContext<object>(
					instanceToValidate: transformedModel,
					propertyChain: new PropertyChain(),
					validatorSelector: new MemberNameValidatorSelector(propertiesToValidate)
				);

			messages.Clear(fieldIdentifier);
			editContext.NotifyValidationStateChanged();

			IEnumerable<IValidator> validators = GetValidatorsForObject(transformedModel, serviceProvider);
			var validationResults = new List<ValidationResult>();

			foreach (IValidator validator in validators)
			{
				var validationResult = await validator.ValidateAsync(fluentValidationContext);
				validationResults.Add(validationResult);
			}

			IEnumerable<string> errorMessages =
				validationResults
				.SelectMany(x => x.Errors)
				.Select(x => x.ErrorMessage)
				.Distinct();

			foreach (string errorMessage in errorMessages)
				messages.Add(fieldIdentifier, errorMessage);

			editContext.NotifyValidationStateChanged();
		}

		private static IEnumerable<IValidator> GetValidatorsForObject(
			object model,
			IServiceProvider serviceProvider)
		{
			var validatorTypesRepository = (FluentValidationRepository)serviceProvider.GetService(typeof(FluentValidationRepository));
			IEnumerable<Type> validatorTypes = validatorTypesRepository.GetValidatorTypesForObject(model);
			IEnumerable<IValidator> validators = validatorTypes.Select(x => (IValidator)serviceProvider.GetService(x));
			return validators;
		}
	}
}
