using System.Collections.Generic;
using System.Linq;
using Moq;
using OrchardCore.ContentManagement.Metadata.Models;
using OrchardCore.ContentTypes.Services;
using OrchardCore.ContentTypes.ViewModels;
using Xunit;

namespace OrchardCore.Tests.Modules.OrchardCore.Localization
{
    public class LocalizationDataProviderTests
    {
        [Fact]
        public void ContentTypeDataLocalizationProvider_GetLocalizedStrings()
        {
            var contentDefinitionService = new Mock<IContentDefinitionService>();
            contentDefinitionService.Setup(cds => cds.GetTypes())
                .Returns(() => new List<EditTypeViewModel> {
                        new EditTypeViewModel { DisplayName = "Article" },
                        new EditTypeViewModel { DisplayName = "BlogPost" },
                        new EditTypeViewModel { DisplayName = "News" }
                    });
            var localizationDataProvider = new ContentTypeDataLocalizationProvider(contentDefinitionService.Object);
            var localizedStrings = localizationDataProvider.GetAllStrings();

            Assert.Equal(3, localizedStrings.Count());
            Assert.True(localizedStrings.All(s => s.Context == "Content Types"));
        }

        [Fact]
        public void ContentFieldDataLocalizationProvider_GetLocalizedStrings()
        {
            var contentDefinitionService = new Mock<IContentDefinitionService>();
            contentDefinitionService.Setup(cds => cds.GetTypes())
                .Returns(() => new List<EditTypeViewModel>
                {
                    new EditTypeViewModel { DisplayName = "BlogPost", TypeDefinition = CreateContentTypeDefinition("BlogPost", "Blog Post", new [] { "Title", "Body", "Author" }) },
                    new EditTypeViewModel { DisplayName = "Person", TypeDefinition = CreateContentTypeDefinition("Person", "Person",  new [] { "FirstName", "LastName" }) },
                });
            var localizationDataProvider = new ContentFieldDataLocalizationProvider(contentDefinitionService.Object);
            var localizedStrings = localizationDataProvider.GetAllStrings();

            Assert.Equal(5, localizedStrings.Count());
            Assert.True(localizedStrings.All(s => s.Context == "Content Fields"));
        }

        private ContentTypeDefinition CreateContentTypeDefinition(string name, string displayName, string[] fields)
        {
            var contentPartFieldDefinitions = new List<ContentPartFieldDefinition>();
            
            foreach (var field in fields)
            {
                contentPartFieldDefinitions.Add(new ContentPartFieldDefinition(new ContentFieldDefinition("TextField"), field, null));
            }

            return new ContentTypeDefinition(
                name,
                displayName,
                new List<ContentTypePartDefinition> { new ContentTypePartDefinition("Part", new ContentPartDefinition("Part", contentPartFieldDefinitions, null), null) },
                null);
        }
    }
}