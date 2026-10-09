--
-- PostgreSQL database dump
--

\restrict bMOFSCNIKVyrYLIcTHh7irSz2z706r28JLyXIWSj3Yco3Tuk33oXAKgh0XKdJim

-- Dumped from database version 17.11 (Debian 17.11-1.pgdg12+2)
-- Dumped by pg_dump version 17.11 (Debian 17.11-1.pgdg12+2)

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: vector; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS vector WITH SCHEMA public;


--
-- Name: EXTENSION vector; Type: COMMENT; Schema: -; Owner: -
--

COMMENT ON EXTENSION vector IS 'vector data type and ivfflat and hnsw access methods';


--
-- Name: EmailTokenPurpose; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public."EmailTokenPurpose" AS ENUM (
    'verify',
    'reset',
    'invite'
);


--
-- Name: InventoryMovementType; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public."InventoryMovementType" AS ENUM (
    'manual',
    'order_reserve',
    'order_release',
    'return'
);


--
-- Name: ProductStatus; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public."ProductStatus" AS ENUM (
    'draft',
    'active',
    'hidden',
    'discontinued'
);


--
-- Name: UserKind; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public."UserKind" AS ENUM (
    'customer',
    'staff'
);


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: _prisma_migrations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public._prisma_migrations (
    id character varying(36) NOT NULL,
    checksum character varying(64) NOT NULL,
    finished_at timestamp with time zone,
    migration_name character varying(255) NOT NULL,
    logs text,
    rolled_back_at timestamp with time zone,
    started_at timestamp with time zone DEFAULT now() NOT NULL,
    applied_steps_count integer DEFAULT 0 NOT NULL
);


--
-- Name: ai_embedding_cache; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ai_embedding_cache (
    id text NOT NULL,
    key text NOT NULL,
    model text NOT NULL,
    embedding double precision[],
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: ai_settings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ai_settings (
    id text DEFAULT 'singleton'::text NOT NULL,
    "monthlyBudgetUsd" double precision NOT NULL,
    "chatDailyLimitUser" integer NOT NULL,
    "chatDailyLimitAnon" integer NOT NULL,
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "updatedAt" timestamp(3) without time zone NOT NULL
);


--
-- Name: ai_usage_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ai_usage_logs (
    id text NOT NULL,
    feature text NOT NULL,
    provider text NOT NULL,
    model text NOT NULL,
    "promptVersion" text,
    "inputTokens" integer DEFAULT 0 NOT NULL,
    "outputTokens" integer DEFAULT 0 NOT NULL,
    "cachedTokens" integer DEFAULT 0 NOT NULL,
    "costUsd" double precision DEFAULT 0 NOT NULL,
    "latencyMs" integer NOT NULL,
    status text NOT NULL,
    "errorCode" text,
    "actorKeyHash" text,
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: audit_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.audit_logs (
    id text NOT NULL,
    "actorId" text NOT NULL,
    action text NOT NULL,
    "targetType" text NOT NULL,
    "targetId" text NOT NULL,
    before jsonb,
    after jsonb,
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: brands; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.brands (
    id text NOT NULL,
    name text NOT NULL,
    slug text NOT NULL,
    "logoPublicId" text,
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "updatedAt" timestamp(3) without time zone NOT NULL
);


--
-- Name: categories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.categories (
    id text NOT NULL,
    "parentId" text,
    name text NOT NULL,
    slug text NOT NULL,
    "position" integer DEFAULT 0 NOT NULL,
    attributes jsonb NOT NULL,
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "updatedAt" timestamp(3) without time zone NOT NULL
);


--
-- Name: consent_records; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.consent_records (
    id text NOT NULL,
    "anonymousId" text NOT NULL,
    "userId" text,
    personalization boolean DEFAULT false NOT NULL,
    marketing boolean DEFAULT false NOT NULL,
    "policyVersion" text NOT NULL,
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: email_tokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.email_tokens (
    id text NOT NULL,
    "tokenHash" text NOT NULL,
    "userId" text NOT NULL,
    purpose public."EmailTokenPurpose" NOT NULL,
    "expiresAt" timestamp(3) without time zone NOT NULL,
    "usedAt" timestamp(3) without time zone,
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: image_deletion_jobs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.image_deletion_jobs (
    id text NOT NULL,
    "publicId" text NOT NULL,
    status text DEFAULT 'pending'::text NOT NULL,
    attempts integer DEFAULT 0 NOT NULL,
    "maxAttempts" integer DEFAULT 8 NOT NULL,
    "nextRetryAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "lastError" text,
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "updatedAt" timestamp(3) without time zone NOT NULL
);


--
-- Name: inventory_movements; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.inventory_movements (
    id text NOT NULL,
    "skuId" text NOT NULL,
    delta integer NOT NULL,
    type public."InventoryMovementType" NOT NULL,
    "orderId" text,
    "actorId" text,
    reason text NOT NULL,
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: oauth_accounts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.oauth_accounts (
    id text NOT NULL,
    provider text NOT NULL,
    "providerUserId" text NOT NULL,
    "userId" text NOT NULL,
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: product_images; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_images (
    id text NOT NULL,
    "productId" text NOT NULL,
    "skuId" text,
    "publicId" text NOT NULL,
    alt text NOT NULL,
    "position" integer DEFAULT 0 NOT NULL,
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: product_slug_history; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_slug_history (
    id text NOT NULL,
    "oldSlug" text NOT NULL,
    "productId" text NOT NULL,
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: products; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.products (
    id text NOT NULL,
    "categoryId" text NOT NULL,
    "brandId" text NOT NULL,
    name text NOT NULL,
    slug text NOT NULL,
    "shortDesc" text NOT NULL,
    description jsonb NOT NULL,
    specs jsonb NOT NULL,
    options jsonb NOT NULL,
    "warrantyMonths" integer DEFAULT 12 NOT NULL,
    status public."ProductStatus" DEFAULT 'draft'::public."ProductStatus" NOT NULL,
    "minPrice" integer,
    "inStock" boolean DEFAULT false NOT NULL,
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "updatedAt" timestamp(3) without time zone NOT NULL
);


--
-- Name: role_permissions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.role_permissions (
    "roleId" text NOT NULL,
    permission text NOT NULL
);


--
-- Name: roles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.roles (
    id text NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    "isSystem" boolean DEFAULT false NOT NULL,
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: sessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sessions (
    id text NOT NULL,
    "tokenHash" text NOT NULL,
    "userId" text NOT NULL,
    "expiresAt" timestamp(3) without time zone NOT NULL,
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: skus; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.skus (
    id text NOT NULL,
    "productId" text NOT NULL,
    code text NOT NULL,
    "optionValues" jsonb NOT NULL,
    price integer NOT NULL,
    "compareAtPrice" integer,
    stock integer DEFAULT 0 NOT NULL,
    enabled boolean DEFAULT true NOT NULL,
    "weightGrams" integer,
    "lengthCm" integer,
    "widthCm" integer,
    "heightCm" integer,
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "updatedAt" timestamp(3) without time zone NOT NULL,
    CONSTRAINT skus_stock_non_negative CHECK ((stock >= 0))
);


--
-- Name: user_roles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_roles (
    "userId" text NOT NULL,
    "roleId" text NOT NULL
);


--
-- Name: users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.users (
    id text NOT NULL,
    email text NOT NULL,
    "emailVerified" boolean DEFAULT false NOT NULL,
    "passwordHash" text,
    name text,
    phone text,
    kind public."UserKind" NOT NULL,
    "lockedAt" timestamp(3) without time zone,
    "deletedAt" timestamp(3) without time zone,
    "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "updatedAt" timestamp(3) without time zone NOT NULL
);


--
-- Name: _prisma_migrations _prisma_migrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public._prisma_migrations
    ADD CONSTRAINT _prisma_migrations_pkey PRIMARY KEY (id);


--
-- Name: ai_embedding_cache ai_embedding_cache_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_embedding_cache
    ADD CONSTRAINT ai_embedding_cache_pkey PRIMARY KEY (id);


--
-- Name: ai_settings ai_settings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_settings
    ADD CONSTRAINT ai_settings_pkey PRIMARY KEY (id);


--
-- Name: ai_usage_logs ai_usage_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_usage_logs
    ADD CONSTRAINT ai_usage_logs_pkey PRIMARY KEY (id);


--
-- Name: audit_logs audit_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_logs
    ADD CONSTRAINT audit_logs_pkey PRIMARY KEY (id);


--
-- Name: brands brands_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.brands
    ADD CONSTRAINT brands_pkey PRIMARY KEY (id);


--
-- Name: categories categories_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categories
    ADD CONSTRAINT categories_pkey PRIMARY KEY (id);


--
-- Name: consent_records consent_records_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.consent_records
    ADD CONSTRAINT consent_records_pkey PRIMARY KEY (id);


--
-- Name: email_tokens email_tokens_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.email_tokens
    ADD CONSTRAINT email_tokens_pkey PRIMARY KEY (id);


--
-- Name: image_deletion_jobs image_deletion_jobs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.image_deletion_jobs
    ADD CONSTRAINT image_deletion_jobs_pkey PRIMARY KEY (id);


--
-- Name: inventory_movements inventory_movements_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_movements
    ADD CONSTRAINT inventory_movements_pkey PRIMARY KEY (id);


--
-- Name: oauth_accounts oauth_accounts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.oauth_accounts
    ADD CONSTRAINT oauth_accounts_pkey PRIMARY KEY (id);


--
-- Name: product_images product_images_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_images
    ADD CONSTRAINT product_images_pkey PRIMARY KEY (id);


--
-- Name: product_slug_history product_slug_history_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_slug_history
    ADD CONSTRAINT product_slug_history_pkey PRIMARY KEY (id);


--
-- Name: products products_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT products_pkey PRIMARY KEY (id);


--
-- Name: role_permissions role_permissions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT role_permissions_pkey PRIMARY KEY ("roleId", permission);


--
-- Name: roles roles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT roles_pkey PRIMARY KEY (id);


--
-- Name: sessions sessions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sessions
    ADD CONSTRAINT sessions_pkey PRIMARY KEY (id);


--
-- Name: skus skus_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.skus
    ADD CONSTRAINT skus_pkey PRIMARY KEY (id);


--
-- Name: user_roles user_roles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_roles
    ADD CONSTRAINT user_roles_pkey PRIMARY KEY ("userId", "roleId");


--
-- Name: users users_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);


--
-- Name: ai_embedding_cache_key_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ai_embedding_cache_key_key ON public.ai_embedding_cache USING btree (key);


--
-- Name: ai_embedding_cache_model_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ai_embedding_cache_model_idx ON public.ai_embedding_cache USING btree (model);


--
-- Name: ai_usage_logs_actorKeyHash_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "ai_usage_logs_actorKeyHash_idx" ON public.ai_usage_logs USING btree ("actorKeyHash");


--
-- Name: ai_usage_logs_createdAt_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "ai_usage_logs_createdAt_idx" ON public.ai_usage_logs USING btree ("createdAt");


--
-- Name: ai_usage_logs_feature_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ai_usage_logs_feature_idx ON public.ai_usage_logs USING btree (feature);


--
-- Name: ai_usage_logs_status_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ai_usage_logs_status_idx ON public.ai_usage_logs USING btree (status);


--
-- Name: audit_logs_action_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX audit_logs_action_idx ON public.audit_logs USING btree (action);


--
-- Name: audit_logs_actorId_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "audit_logs_actorId_idx" ON public.audit_logs USING btree ("actorId");


--
-- Name: audit_logs_createdAt_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "audit_logs_createdAt_idx" ON public.audit_logs USING btree ("createdAt");


--
-- Name: brands_slug_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX brands_slug_key ON public.brands USING btree (slug);


--
-- Name: categories_slug_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX categories_slug_key ON public.categories USING btree (slug);


--
-- Name: consent_records_anonymousId_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "consent_records_anonymousId_idx" ON public.consent_records USING btree ("anonymousId");


--
-- Name: consent_records_userId_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "consent_records_userId_idx" ON public.consent_records USING btree ("userId");


--
-- Name: email_tokens_tokenHash_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "email_tokens_tokenHash_key" ON public.email_tokens USING btree ("tokenHash");


--
-- Name: email_tokens_userId_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "email_tokens_userId_idx" ON public.email_tokens USING btree ("userId");


--
-- Name: image_deletion_jobs_status_nextRetryAt_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "image_deletion_jobs_status_nextRetryAt_idx" ON public.image_deletion_jobs USING btree (status, "nextRetryAt");


--
-- Name: inventory_movements_orderId_skuId_type_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "inventory_movements_orderId_skuId_type_key" ON public.inventory_movements USING btree ("orderId", "skuId", type);


--
-- Name: inventory_movements_skuId_createdAt_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "inventory_movements_skuId_createdAt_idx" ON public.inventory_movements USING btree ("skuId", "createdAt");


--
-- Name: oauth_accounts_provider_providerUserId_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "oauth_accounts_provider_providerUserId_key" ON public.oauth_accounts USING btree (provider, "providerUserId");


--
-- Name: oauth_accounts_userId_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "oauth_accounts_userId_idx" ON public.oauth_accounts USING btree ("userId");


--
-- Name: product_images_productId_position_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "product_images_productId_position_idx" ON public.product_images USING btree ("productId", "position");


--
-- Name: product_slug_history_oldSlug_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "product_slug_history_oldSlug_key" ON public.product_slug_history USING btree ("oldSlug");


--
-- Name: product_slug_history_productId_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "product_slug_history_productId_idx" ON public.product_slug_history USING btree ("productId");


--
-- Name: products_categoryId_status_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "products_categoryId_status_idx" ON public.products USING btree ("categoryId", status);


--
-- Name: products_createdAt_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "products_createdAt_idx" ON public.products USING btree ("createdAt");


--
-- Name: products_minPrice_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "products_minPrice_idx" ON public.products USING btree ("minPrice");


--
-- Name: products_slug_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX products_slug_key ON public.products USING btree (slug);


--
-- Name: products_specs_gin_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX products_specs_gin_idx ON public.products USING gin (specs);


--
-- Name: roles_code_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX roles_code_key ON public.roles USING btree (code);


--
-- Name: sessions_tokenHash_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "sessions_tokenHash_key" ON public.sessions USING btree ("tokenHash");


--
-- Name: sessions_userId_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "sessions_userId_idx" ON public.sessions USING btree ("userId");


--
-- Name: skus_code_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX skus_code_key ON public.skus USING btree (code);


--
-- Name: skus_productId_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "skus_productId_idx" ON public.skus USING btree ("productId");


--
-- Name: users_email_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX users_email_key ON public.users USING btree (email);


--
-- Name: categories categories_parentId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categories
    ADD CONSTRAINT "categories_parentId_fkey" FOREIGN KEY ("parentId") REFERENCES public.categories(id) ON UPDATE CASCADE ON DELETE RESTRICT;


--
-- Name: consent_records consent_records_userId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.consent_records
    ADD CONSTRAINT "consent_records_userId_fkey" FOREIGN KEY ("userId") REFERENCES public.users(id) ON UPDATE CASCADE ON DELETE SET NULL;


--
-- Name: email_tokens email_tokens_userId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.email_tokens
    ADD CONSTRAINT "email_tokens_userId_fkey" FOREIGN KEY ("userId") REFERENCES public.users(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- Name: inventory_movements inventory_movements_skuId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_movements
    ADD CONSTRAINT "inventory_movements_skuId_fkey" FOREIGN KEY ("skuId") REFERENCES public.skus(id) ON UPDATE CASCADE ON DELETE RESTRICT;


--
-- Name: oauth_accounts oauth_accounts_userId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.oauth_accounts
    ADD CONSTRAINT "oauth_accounts_userId_fkey" FOREIGN KEY ("userId") REFERENCES public.users(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- Name: product_images product_images_productId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_images
    ADD CONSTRAINT "product_images_productId_fkey" FOREIGN KEY ("productId") REFERENCES public.products(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- Name: product_images product_images_skuId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_images
    ADD CONSTRAINT "product_images_skuId_fkey" FOREIGN KEY ("skuId") REFERENCES public.skus(id) ON UPDATE CASCADE ON DELETE SET NULL;


--
-- Name: product_slug_history product_slug_history_productId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_slug_history
    ADD CONSTRAINT "product_slug_history_productId_fkey" FOREIGN KEY ("productId") REFERENCES public.products(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- Name: products products_brandId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT "products_brandId_fkey" FOREIGN KEY ("brandId") REFERENCES public.brands(id) ON UPDATE CASCADE ON DELETE RESTRICT;


--
-- Name: products products_categoryId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT "products_categoryId_fkey" FOREIGN KEY ("categoryId") REFERENCES public.categories(id) ON UPDATE CASCADE ON DELETE RESTRICT;


--
-- Name: role_permissions role_permissions_roleId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT "role_permissions_roleId_fkey" FOREIGN KEY ("roleId") REFERENCES public.roles(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- Name: sessions sessions_userId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sessions
    ADD CONSTRAINT "sessions_userId_fkey" FOREIGN KEY ("userId") REFERENCES public.users(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- Name: skus skus_productId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.skus
    ADD CONSTRAINT "skus_productId_fkey" FOREIGN KEY ("productId") REFERENCES public.products(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- Name: user_roles user_roles_roleId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_roles
    ADD CONSTRAINT "user_roles_roleId_fkey" FOREIGN KEY ("roleId") REFERENCES public.roles(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- Name: user_roles user_roles_userId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_roles
    ADD CONSTRAINT "user_roles_userId_fkey" FOREIGN KEY ("userId") REFERENCES public.users(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

\unrestrict bMOFSCNIKVyrYLIcTHh7irSz2z706r28JLyXIWSj3Yco3Tuk33oXAKgh0XKdJim

