﻿using AutoMapper;
using Fan.Data;
using Fan.Enums;
using Fan.Exceptions;
using Fan.Helpers;
using Fan.Models;
using Fan.Validators;
using FluentValidation.Results;
using Humanizer;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Fan.Services
{
    public class BlogService : IBlogService
    {
        private readonly ICategoryRepository _catRepo;
        private readonly IMetaRepository _metaRepo;
        private readonly IPostRepository _postRepo;
        private readonly ITagRepository _tagRepo;
        private readonly IDistributedCache _cache;
        private readonly ILogger _logger;
        private readonly IMapper _mapper;

        public BlogService(ICategoryRepository catRepo,
            IMetaRepository metaRepo,
            IPostRepository postRepo,
            ITagRepository tagRepo,
            IDistributedCache cache,
            ILoggerFactory loggerFactory,
            IMapper mapper)
        {
            _catRepo = catRepo;
            _metaRepo = metaRepo;
            _postRepo = postRepo;
            _tagRepo = tagRepo;
            _cache = cache;
            _mapper = mapper;
            _logger = loggerFactory.CreateLogger<BlogService>();
        }

        public const string CACHE_KEY_ALL_CATS = "BlogCategories";
        public const string CACHE_KEY_ALL_TAGS = "BlogTags";
        public const string CACHE_KEY_BLOG_SETTINGS = "BlogSettings";
        public const string CACHE_KEY_POSTS_INDEX = "BlogPostsIndex";

        // -------------------------------------------------------------------- Settings

        /// <summary>
        /// Creates <see cref="BlogSettings"/>, if a BlogSettings already exists return it.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public async Task<BlogSettings> CreateSettingsAsync(BlogSettings settings)
        {
            var meta = await _metaRepo.GetAsync("BlogSettings");
            if (meta != null) return settings;

            meta = new Meta
            {
                Key = "BlogSettings",
                Value = JsonConvert.SerializeObject(settings),
            };
            await _metaRepo.CreateAsync(meta);

            return settings;
        }

        /// <summary>
        /// Returns the <see cref="BlogSettings"/>, returns null if it does not exist.
        /// </summary>
        /// <remarks>
        /// A <see cref="BlogSettings"/> is created when the application runs for the first time.
        /// </remarks>
        public async Task<BlogSettings> GetSettingsAsync()
        {
            return await _cache.GetAsync<BlogSettings>(CACHE_KEY_BLOG_SETTINGS, new TimeSpan(0, 10, 0), async () =>
            {
                var meta = await _metaRepo.GetAsync("BlogSettings");

                return (meta == null) ? null : JsonConvert.DeserializeObject<BlogSettings>(meta.Value);
            });
        }

        /// <summary>
        /// Updates the <see cref="BlogSettings"/>, if BlogSettings does not exist, it will throw <see cref="FanException"/>.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public async Task<BlogSettings> UpdateSettingsAsync(BlogSettings settings)
        {
            var meta = await _metaRepo.GetAsync("BlogSettings");
            if (meta == null)
            {
                throw new FanException("BlogSettings cannot be updated because it does not exist in the system.");
            }

            meta.Value = JsonConvert.SerializeObject(settings);

            await _metaRepo.UpdateAsync(meta);
            await _cache.RemoveAsync(CACHE_KEY_BLOG_SETTINGS);

            return settings;
        }

        // -------------------------------------------------------------------- Categories

        /// <summary>
        /// Creates a <see cref="Category"/> and invalidates cache for all categories.
        /// </summary>
        /// <param name="category"></param>
        /// <returns></returns>
        public async Task<Category> CreateCategoryAsync(Category category)
        {
            category = await this.PrepTaxonomyAsync(category, ECreateOrUpdate.Create) as Category;
            category = await _catRepo.CreateAsync(category);
            await _cache.RemoveAsync(CACHE_KEY_ALL_CATS); // TODO what about posts

            return category;
        }

        /// <summary>
        /// Deletes a <see cref="Category"/> and reassigns posts to a default category, and 
        /// invalidates caceh for all categories.  Throws <see cref="FanException"/> if the
        /// category being deleted is the default category.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        /// <remarks>
        /// The default category cannot be deleted, on the UI there is no delete button available
        /// for default category, thus when there is only one category left, it'll be the default,
        /// and you'll always have it available.
        /// </remarks>
        public async Task DeleteCategoryAsync(int id)
        {
            var blogSettings = await this.GetSettingsAsync();

            // on the UI there is no delete button on the default cat
            // therefore when there is only one category left, it'll be the default.
            if (id == blogSettings.DefaultCategoryId)
            {
                throw new FanException("Default category cannot be deleted.");
            }

            await _catRepo.DeleteAsync(id, blogSettings.DefaultCategoryId);
            await _cache.RemoveAsync(CACHE_KEY_ALL_CATS);
        }

        /// <summary>
        /// Returns category by id, throws <see cref="FanException"/> if category with id is not found.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<Category> GetCategoryAsync(int id)
        {
            var cats = await this.GetCategoriesAsync();
            var cat = cats.SingleOrDefault(c => c.Id == id);
            if (cat == null)
            {
                throw new FanException("Category is not found.");
            }

            return cat;
        }

        /// <summary>
        /// Returns category by slug, throws <see cref="FanException"/> if category with slug is not found.
        /// </summary>
        /// <param name="slug"></param>
        /// <returns></returns>
        public async Task<Category> GetCategoryAsync(string slug)
        {
            var cats = await this.GetCategoriesAsync();
            var cat = cats.SingleOrDefault(c => c.Slug.Equals(slug, StringComparison.CurrentCultureIgnoreCase));
            if (cat == null)
            {
                throw new FanException("Category is not found.");
            }

            return cat;
        }

        /// <summary>
        /// Returns all categories, cached after calls to DAL.
        /// </summary>
        /// <returns></returns>
        public async Task<List<Category>> GetCategoriesAsync()
        {
            return await _cache.GetAsync<List<Category>>(CACHE_KEY_ALL_CATS, new TimeSpan(0, 10, 0), async () => {
                return await _catRepo.GetListAsync();
            });
        }

        /// <summary>
        /// Updates a <see cref="Category"/> and invalidates cache for all categories.
        /// </summary>
        /// <param name="category"></param>
        /// <returns></returns>
        public async Task<Category> UpdateCategoryAsync(Category category)
        {
            category = await this.PrepTaxonomyAsync(category, ECreateOrUpdate.Update) as Category;
            category = await _catRepo.UpdateAsync(category);
            await _cache.RemoveAsync(CACHE_KEY_ALL_CATS);

            return category;
        }

        // -------------------------------------------------------------------- Tags

        /// <summary>
        /// Creates a <see cref="Tag"/> and invalidates cache for all tags.
        /// </summary>
        /// <param name="tag"></param>
        /// <returns></returns>
        public async Task<Tag> CreateTagAsync(Tag tag)
        {
            tag = await this.PrepTaxonomyAsync(tag, ECreateOrUpdate.Create) as Tag;
            tag = await _tagRepo.CreateAsync(tag);
            await _cache.RemoveAsync(CACHE_KEY_ALL_TAGS);

            return tag;
        }

        /// <summary>
        /// Deletes a <see cref="Tag"/> by id and invalidates cache for all tags.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task DeleteTagAsync(int id)
        {
            await _tagRepo.DeleteAsync(id);
            await _cache.RemoveAsync(CACHE_KEY_ALL_TAGS);
        }

        /// <summary>
        /// Returns tag by id, throws <see cref="FanException"/> if tag with id is not found.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<Tag> GetTagAsync(int id)
        {
            var tags = await this.GetTagsAsync();
            var tag = tags.SingleOrDefault(c => c.Id == id);
            if (tag == null)
            {
                throw new FanException("Tag is not found.");
            }

            return tag;
        }

        /// <summary>
        /// Returns tag by slug, throws <see cref="FanException"/> if tag with slug is not found.
        /// </summary>
        /// <param name="slug"></param>
        /// <returns></returns>
        public async Task<Tag> GetTagAsync(string slug)
        {
            var tags = await this.GetTagsAsync();
            var tag = tags.SingleOrDefault(c => c.Slug.Equals(slug, StringComparison.CurrentCultureIgnoreCase));
            if (tag == null)
            {
                throw new FanException("Tag is not found.");
            }

            return tag;
        }

        /// <summary>
        /// Returns all tags, cached after calls to DAL.
        /// </summary>
        /// <returns></returns>
        public async Task<List<Tag>> GetTagsAsync()
        {
            return await _cache.GetAsync<List<Tag>>(CACHE_KEY_ALL_TAGS, new TimeSpan(0, 10, 0), async () => {
                return await _tagRepo.GetListAsync();
            });
        }

        /// <summary>
        /// Updates a <see cref="Tag"/> and invalidates cache for all tags.
        /// </summary>
        /// <param name="tag"></param>
        /// <returns></returns>
        public async Task<Tag> UpdateTagAsync(Tag tag)
        {
            tag = await this.PrepTaxonomyAsync(tag, ECreateOrUpdate.Update) as Tag;
            tag = await _tagRepo.UpdateAsync(tag);
            await _cache.RemoveAsync(CACHE_KEY_ALL_TAGS);

            return tag;
        }

        // -------------------------------------------------------------------- Media

        /// <summary>
        /// Creates or updates a media post.
        /// </summary>
        /// <param name="media"></param>
        /// <returns></returns>
        public async Task<Media> UpsertMediaAsync(Media media)
        {
            if (media == null) return media;

            var theMedia = await this.GetMediaAsync(media.Slug);

            if (theMedia != null)
            {
                theMedia.UserName = media.UserName;
                theMedia.Slug = media.Slug;
                theMedia.Title = media.Title;
                theMedia.MimeType = media.MimeType;
                theMedia.UpdatedOn = DateTime.UtcNow;

                var post = _mapper.Map<Media, Post>(theMedia);
                await _postRepo.UpdateAsync(post);
            }
            else
            {
                media.CreatedOn = DateTime.UtcNow;
                var post = _mapper.Map<Media, Post>(media);
                await _postRepo.CreateAsync(post);
            }

            return media;
        }

        /// <summary>
        /// Returns a media post by slug, returns null if not found.
        /// </summary>
        /// <param name="slug"></param>
        /// <returns></returns>
        public async Task<Media> GetMediaAsync(string slug)
        {
            var post = await _postRepo.GetAsync(slug, EPostType.Media);
            return _mapper.Map<Post, Media>(post);
        }

        // -------------------------------------------------------------------- BlogPosts 

        /// <summary>
        /// Creates a <see cref="BlogPost"/> and invalidates cache for posts on index page.
        /// </summary>
        /// <param name="blogPost"></param>
        /// <returns></returns>
        public async Task<BlogPost> CreatePostAsync(BlogPost blogPost)
        {
            // blogPost is just used as a container of data
            if (blogPost == null) return blogPost;

            // preps BlogPost for saving and gets Post back
            var post = await this.PrepPostAsync(blogPost, ECreateOrUpdate.Create);

            // save post
            await _postRepo.CreateAsync(post);

            // invalidate cache only when published
            if (blogPost.Status == EPostStatus.Published)
            {
                await _cache.RemoveAsync(CACHE_KEY_POSTS_INDEX);
            }

            // get BlogPost again
            return await this.GetPostAsync(post.Id);
        }

        /// <summary>
        /// Updates a <see cref="BlogPost"/> and invalidates caceh for posts on index page.
        /// </summary>
        /// <param name="blogPost"></param>
        /// <returns></returns>
        public async Task<BlogPost> UpdatePostAsync(BlogPost blogPost)
        {
            // blogPost is just used as a container of data
            if (blogPost == null) return blogPost;

            var post = await this.PrepPostAsync(blogPost, ECreateOrUpdate.Update);

            // update
            await _postRepo.UpdateAsync(post);

            // invalidate cache 
            await _cache.RemoveAsync(CACHE_KEY_POSTS_INDEX);

            // return a new blogPost with latest data
            return await this.GetPostAsync(post.Id);
        }

        /// <summary>
        /// Deletes a blog post and invalidates cache for posts on index page.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task DeletePostAsync(int id)
        {
            await _postRepo.DeleteAsync(id);
            await _cache.RemoveAsync(CACHE_KEY_POSTS_INDEX);
        }

        /// <summary>
        /// Returns a <see cref="BlogPost"/> by id with its <see cref="Category"/> and <see cref="Tag"/>
        /// and throws <see cref="FanException"/> if not found.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        /// <exception cref="FanException">if post is not found.</exception>
        /// <remarks>
        /// This is used when you want to get a post from db.
        /// </remarks>
        public async Task<BlogPost> GetPostAsync(int id)
        {
            var post = await this.GetPostAsync(id, EPostType.BlogPost);
            return await this.PrepBlogPostForShowingAsync(post);
        }

        /// <summary>
        /// Returns a <see cref="BlogPost"/> by slug and date time, throws <see cref="FanException"/> if not found.
        /// </summary>
        /// <param name="slug"></param>
        /// <param name="year"></param>
        /// <param name="month"></param>
        /// <param name="day"></param>
        /// <exception cref="FanException">if post is not found.</exception>
        /// <remarks>
        /// This is used by controller. The dates are stored as UTC time in database, thus the 
        /// params year, month and day should be the UTC time.
        /// </remarks>
        public async Task<BlogPost> GetPostAsync(string slug, int year, int month, int day)
        {
            // todo caching
            var post = await _postRepo.GetAsync(slug, year, month, day);

            return await this.PrepBlogPostForShowingAsync(post);
        }

        /// <summary>
        /// Returns a list of blog posts for the blog index page.
        /// </summary>
        /// <param name="pageIndex"></param>
        /// <returns></returns>
        public async Task<BlogPostList> GetPostsAsync(int pageIndex)
        {
            PostListQuery query = new PostListQuery(EPostListQueryType.BlogPosts)
            {
                PageIndex = (pageIndex <= 0) ? 1 : pageIndex,
                PageSize = (await GetSettingsAsync()).PageSize,
            };

            // cache only first page
            if (query.PageIndex == 1)
            {
                return await _cache.GetAsync<BlogPostList>(CACHE_KEY_POSTS_INDEX, new TimeSpan(0, 10, 0), async () =>
                {
                    return await this.QueryPostsAsync(query);
                });
            }

            return await this.QueryPostsAsync(query);
        }

        /// <summary>
        /// Returns a list of blog posts for a category.
        /// </summary>
        /// <param name="categorySlug"></param>
        /// <param name="pageIndex"></param>
        /// <returns></returns>
        public async Task<BlogPostList> GetPostsForCategoryAsync(string categorySlug, int pageIndex)
        {
            // todo caching
            PostListQuery query = new PostListQuery(EPostListQueryType.BlogPostsByCategory)
            {
                CategorySlug = categorySlug,
                PageIndex = (pageIndex <= 0) ? 1 : pageIndex,
                PageSize = (await GetSettingsAsync()).PageSize,
            };

            return await this.QueryPostsAsync(query);
        }

        /// <summary>
        /// Returns a list of blog posts for a tag.
        /// </summary>
        /// <param name="tagSlug"></param>
        /// <param name="pageIndex"></param>
        /// <returns></returns>
        public async Task<BlogPostList> GetPostsForTagAsync(string tagSlug, int pageIndex)
        {
            // todo caching
            PostListQuery query = new PostListQuery(EPostListQueryType.BlogPostsByTag)
            {
                TagSlug = tagSlug,
                PageIndex = (pageIndex <= 0) ? 1 : pageIndex,
                PageSize = (await GetSettingsAsync()).PageSize,
            };

            return await this.QueryPostsAsync(query);
        }

        /// <summary>
        /// Returns a list of blog drafts.
        /// </summary>
        /// <returns></returns>
        public async Task<List<BlogPost>> GetPostsForDraftsAsync()
        {
            PostListQuery query = new PostListQuery(EPostListQueryType.BlogDrafts);

            return await this.QueryPostsAsync(query);
        }

        /// <summary>
        /// Returns specified number of <see cref="BlogPost"/> used by metaweblog.
        /// </summary>
        /// <param name="numberOfPosts">"All" is int.MaxValue</param>
        public async Task<List<BlogPost>> GetRecentPostsAsync(int numberOfPosts)
        {
            var query = new PostListQuery(EPostListQueryType.BlogPostsByNumber) { PageSize = numberOfPosts };

            return await this.QueryPostsAsync(query);
        }

        // -------------------------------------------------------------------- Private

        /// <summary>
        /// Returns a <see cref="Post"/>, throws <see cref="FanException"/> if not found.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        /// <remarks>
        /// This returns Post not a BlogPost, which would maintain tracking for <see cref="PrepPostAsync(BlogPost, string)"/>.
        /// </remarks>
        private async Task<Post> GetPostAsync(int id, EPostType type)
        {
            var post = await _postRepo.GetAsync(id, type);
            if (post == null)
            {
                _logger.LogWarning($"{type} with id {id} is not found!");
                throw new FanException($"{type} with id {id} is not found.");
            }

            return post;
        }

        /// <summary>
        /// Prepares a category or tag for create or update, making sure its title and slug are valid.
        /// </summary>
        /// <param name="tax">A category or tag.</param>
        /// <param name="createOrUpdate"></param>
        /// <returns></returns>
        private async Task<Taxonomy> PrepTaxonomyAsync(Taxonomy tax, ECreateOrUpdate createOrUpdate)
        {
            // get existing titles and slugs
            IEnumerable<string> existingTitles = null;
            IEnumerable<string> existingSlugs = null;
            ETaxonomyType type = ETaxonomyType.Category;
            if (tax is Category)
            {
                var allCats = await this.GetCategoriesAsync();
                existingTitles = allCats.Select(c => c.Title);
                existingSlugs = allCats.Select(c => c.Slug);
            }
            else
            {
                var allTags = await this.GetTagsAsync();
                existingTitles = allTags.Select(c => c.Title);
                existingSlugs = allTags.Select(c => c.Slug);
                type = ETaxonomyType.Tag;
            }

            // validator
            var validator = new TaxonomyValidator(existingTitles, type);
            ValidationResult result = await validator.ValidateAsync(tax);
            if (!result.IsValid)
            {
                var cou = createOrUpdate.ToString().ToLower();
                throw new FanException($"Failed to {cou} {type}.", result.Errors);
            }

            // Slug: user can create / update slug, we format the slug if it's available else we 
            // use title to get the slug.
            tax.Slug = BlogServiceHelper.FormatTaxonomySlug(tax.Slug.IsNullOrEmpty() ? tax.Title : tax.Slug, existingSlugs);

            // html encode title
            tax.Title = WebUtility.HtmlEncode(tax.Title);

            return tax;
        }

        /// <summary>
        /// Preps a <see cref="BlogPost"/> for create or update.
        /// </summary>
        /// <param name="blogPost"></param>
        /// <param name="createOrUpdate"></param>
        /// <returns></returns>
        private async Task<Post> PrepPostAsync(BlogPost blogPost, ECreateOrUpdate createOrUpdate)
        {
            // Validation
            var validator = new PostValidator();
            ValidationResult result = await validator.ValidateAsync(blogPost);
            if (!result.IsValid)
            {
                throw new FanException($"Failed to {createOrUpdate} blog post.", result.Errors);
            }

            // Get post
            // NOTE: can't use this.GetPostAsync(blogPost.Id) as it returns a BlogPost not a Post which would lose tracking
            var post = (createOrUpdate == ECreateOrUpdate.Create) ? new Post() : await this.GetPostAsync(blogPost.Id, EPostType.BlogPost);
            var blogSettings = await this.GetSettingsAsync();

            // CreatedOn
            if (createOrUpdate == ECreateOrUpdate.Create) // create: if user didn't set a time, server will give it UtcNow which is correct 
            {
                post.CreatedOn = (blogPost.CreatedOn <= DateTime.MinValue) ? DateTime.UtcNow : blogSettings.ToUtc(blogPost.CreatedOn);
            }
            else if (blogPost.CreatedOn != blogSettings.FromUtc(post.CreatedOn)) // update: a change in post time
            {
                post.CreatedOn = (blogPost.CreatedOn <= DateTime.MinValue) ? post.CreatedOn : blogSettings.ToUtc(blogPost.CreatedOn);
            }

            // UpdatedOn
            if (blogPost.Status == EPostStatus.Draft) post.UpdatedOn = post.CreatedOn;
            else post.UpdatedOn = null;

            // Slug before Title
            post.Slug = await this.GetBlogPostSlugAsync(blogPost);
            post.Title = blogPost.Title; // looks like OLW html encodes post title

            // Body & Excerpt, UserName
            post.Body = blogPost.Body.IsNullOrWhiteSpace() ? null : blogPost.Body;
            post.Excerpt = blogPost.Excerpt.IsNullOrWhiteSpace() ? null : blogPost.Excerpt;
            post.UserName = blogPost.UserName;

            // Status & CommentStatus
            post.Status = blogPost.Status;
            post.CommentStatus = blogPost.CommentStatus;

            // Categories TODO check CategoryTitle first
            if (!string.IsNullOrEmpty(blogPost.CategoryTitle)) // CatTitle takes precedence if available
            {
                var cat = (await this.GetCategoriesAsync())
                    .SingleOrDefault(c => c.Title.Equals(blogPost.CategoryTitle, StringComparison.CurrentCultureIgnoreCase));
                if (cat == null)
                    post.Category = await this.CreateCategoryAsync(new Category { Title = blogPost.CategoryTitle });
                else
                    //post.Category = cat; // todo see if id works
                    post.CategoryId = cat.Id;
            }
            else if (blogPost.CategoryId.HasValue) // browser 
            {
                if (createOrUpdate == ECreateOrUpdate.Create)
                    post.CategoryId = blogPost.CategoryId.Value;
                else if (blogPost.CategoryId != post.CategoryId)
                    post.CategoryId = blogPost.CategoryId;
            }
            else
            {
                post.CategoryId = blogSettings.DefaultCategoryId; // TODO test
            }

            // Tags & PosTags
            if (blogPost.TagTitles != null && blogPost.TagTitles.Count > 0)
            {
                // make sure list has no empty strings and only unique values
                blogPost.TagTitles = blogPost.TagTitles.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
                var allTags = await this.GetTagsAsync();

                if (createOrUpdate == ECreateOrUpdate.Create)
                {
                    foreach (var title in blogPost.TagTitles)
                    {
                        var tag = allTags.FirstOrDefault(t => t.Title.Equals(title, StringComparison.CurrentCultureIgnoreCase));
                        if (tag == null)
                            tag = await this.CreateTagAsync(new Tag { Title = title });

                        // NOTE: post.PostTags.Add(new PostTag { Post = post, Tag = tag }); 
                        // would fail with SqlServer, though during testing it worked with Sqlite In-Mem!!
                        // The reason is allTags are not tracked, you will receive the following exception
                        // SqlException: Cannot insert explicit value for identity column in table 'Tag' when IDENTITY_INSERT is set to OFF.
                        
                        //post.PostTags.Add(new PostTag { Post = post, Tag = tag });  // does not work with SqlServer
                        post.PostTags.Add(new PostTag { Post = post, TagId = tag.Id });
                    }
                }
                else
                {
                    var tagTitlesCurrent = post.PostTags.Select(pt => pt.Tag.Title).ToList();

                    var tagsToRemove = tagTitlesCurrent.Except(blogPost.TagTitles).ToList();
                    foreach (var title in tagsToRemove)
                    {
                        post.PostTags.Remove(post.PostTags.Single(pt => pt.Tag.Title == title));
                    }

                    var tagsToAdd = blogPost.TagTitles.Except(tagTitlesCurrent).ToList();
                    foreach (var title in tagsToAdd)
                    {
                        var tag = allTags.FirstOrDefault(t => t.Title.Equals(title, StringComparison.CurrentCultureIgnoreCase));
                        if (tag == null)
                            tag = await this.CreateTagAsync(new Tag { Title = title });

                        // here works because tag is just created so it is tracked
                        post.PostTags.Add(new PostTag { Post = post, Tag = tag });
                    }
                }
            }

            return post;
        }

        /// <summary>
        /// Preps a <see cref="BlogPost"/> for showing.
        /// </summary>
        /// <param name="post"></param>
        /// <returns></returns>
        /// <remarks>
        /// It readies the excerpt and the list of tags.
        /// </remarks>
        private async Task<BlogPost> PrepBlogPostForShowingAsync(Post post)
        {
            var blogPost = _mapper.Map<Post, BlogPost>(post);
            var blogSettings = await this.GetSettingsAsync();

             // Note: browser needs Utc (WP works the same way), so don't convert back to local
             // for example: "post/2017/6/2/slug" is actually posted on the night of 6/1/2017, 
             // but since DB stores UTC, here we must stay that way

            // humanize datetime
            blogPost.CreatedOnDisplay = DateTime.Now.Add(blogSettings.FromUtc(blogPost.CreatedOn) - DateTime.Now).Humanize(utcDate: false);

            // Title
            blogPost.Title = WebUtility.HtmlDecode(blogPost.Title); // since OLW encodes it, we decode it here

            // Excerpt
            blogPost.Excerpt = post.Excerpt.IsNullOrEmpty() ? Util.GetExcerpt(post.Body, blogSettings.ExcerptWordLimit) : post.Excerpt;

            // CategoryTitle
            blogPost.CategoryTitle = post.Category.Title;

            // Tags and TagTitles
            foreach (var postTag in post.PostTags)
            {
                blogPost.Tags.Add(postTag.Tag);
                blogPost.TagTitles.Add(postTag.Tag.Title);
            }

            return blogPost;
        }

        /// <summary>
        /// Returns the slug for a <see cref="BlogPost"/>. The returned slug is unique and valid.
        /// </summary>
        private async Task<string> GetBlogPostSlugAsync(BlogPost blogPost)
        {
            string input = string.IsNullOrEmpty(blogPost.Slug) ? blogPost.Title : blogPost.Slug;

            // when user manually inputted a slug, it could exceed max len
            if (input.Length > Const.POST_TITLE_SLUG_MAXLEN)
            {
                input = input.Substring(0, Const.POST_TITLE_SLUG_MAXLEN);
            }

            // remove/replace odd char, lower case etc
            var slug = Util.FormatSlug(input);

            // slug from title could be empty, e.g. the title is in Chinese
            // then we generate a random string of 6 chars
            if (string.IsNullOrEmpty(slug))
            {
                slug = Util.RandomString(8);
            }

            // make sure slug is unique
            int i = 2;
            if (blogPost.Id <= 0) // create
            {
                while (await _postRepo.GetAsync(slug, blogPost.CreatedOn.Year, blogPost.CreatedOn.Month, blogPost.CreatedOn.Day) != null)
                {
                    slug = $"{slug}-{i}";
                    i++;
                }
            }
            else // update
            {
                var p = await _postRepo.GetAsync(slug, blogPost.CreatedOn.Year, blogPost.CreatedOn.Month, blogPost.CreatedOn.Day);
                while (p != null && p.Id != blogPost.Id)
                {
                    slug = $"{slug}-{i}";
                    i++;
                    p = await _postRepo.GetAsync(slug, blogPost.CreatedOn.Year, blogPost.CreatedOn.Month, blogPost.CreatedOn.Day);
                }
            }

            return slug;
        }

        /// <summary>
        /// Returns a <see cref="BlogPostList"/> based on query.
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        private async Task<BlogPostList> QueryPostsAsync(PostListQuery query)
        {
            var results = await _postRepo.GetListAsync(query);
            var blogPostList = new BlogPostList(results.totalCount, query.PageSize);
            foreach (var post in results.posts)
            {
                blogPostList.Add(await this.PrepBlogPostForShowingAsync(post));
            }
            return blogPostList;
        }
    }
}