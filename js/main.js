document.addEventListener('DOMContentLoaded', function() {
    // Mobile menu toggle
    const mobileToggle = document.querySelector('.nav-mobile-toggle');
    const mobileMenu = document.querySelector('.mobile-menu');

    if (mobileToggle && mobileMenu) {
        mobileToggle.addEventListener('click', () => {
            mobileMenu.classList.toggle('active');
            mobileToggle.classList.toggle('active');
        });

        // Close mobile menu on link click
        document.querySelectorAll('.mobile-link, .mobile-cta').forEach(link => {
            link.addEventListener('click', () => {
                mobileMenu.classList.remove('active');
                mobileToggle.classList.remove('active');
            });
        });
    }

    // Smooth scroll for anchor links
    document.querySelectorAll('a[href^="#"]').forEach(anchor => {
        anchor.addEventListener('click', function (e) {
            const href = this.getAttribute('href');
            if (href && href.length > 1) {
                const target = document.querySelector(href);
                if (target) {
                    e.preventDefault();
                    const navHeight = document.querySelector('.nav')?.offsetHeight || 64;
                    const targetPosition = target.getBoundingClientRect().top + window.pageYOffset - navHeight;
                    window.scrollTo({
                        top: targetPosition,
                        behavior: 'smooth'
                    });
                }
            }
        });
    });

    // Nav background on scroll
    const nav = document.querySelector('.nav');
    if (nav) {
        window.addEventListener('scroll', () => {
            if (window.scrollY > 50) {
                nav.style.background = 'rgba(0, 0, 0, 0.95)';
            } else {
                nav.style.background = 'rgba(0, 0, 0, 0.8)';
            }
        });
    }

    // Countdown Timer - January 9, 2026
    const countdownDate = new Date('January 9, 2026 00:00:00').getTime();

    function updateCountdown() {
        const now = new Date().getTime();
        const distance = countdownDate - now;

        if (distance > 0) {
            const days = Math.floor(distance / (1000 * 60 * 60 * 24));
            const hours = Math.floor((distance % (1000 * 60 * 60 * 24)) / (1000 * 60 * 60));
            const minutes = Math.floor((distance % (1000 * 60 * 60)) / (1000 * 60));
            const seconds = Math.floor((distance % (1000 * 60)) / 1000);

            const daysEl = document.getElementById('days');
            const hoursEl = document.getElementById('hours');
            const minutesEl = document.getElementById('minutes');
            const secondsEl = document.getElementById('seconds');

            if (daysEl) daysEl.textContent = String(days).padStart(2, '0');
            if (hoursEl) hoursEl.textContent = String(hours).padStart(2, '0');
            if (minutesEl) minutesEl.textContent = String(minutes).padStart(2, '0');
            if (secondsEl) secondsEl.textContent = String(seconds).padStart(2, '0');
        }
    }

    updateCountdown();
    setInterval(updateCountdown, 1000);

    // Animate elements on scroll
    const observerOptions = {
        threshold: 0.1,
        rootMargin: '0px 0px -50px 0px'
    };

    const observer = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.style.opacity = '1';
                entry.target.style.transform = 'translateY(0)';
            }
        });
    }, observerOptions);

    document.querySelectorAll('.feature-card, .comparison-card, .tech-item, .pricing-card, .faq-item').forEach(el => {
        el.style.opacity = '0';
        el.style.transform = 'translateY(20px)';
        el.style.transition = 'opacity 0.6s ease, transform 0.6s ease';
        observer.observe(el);
    });

    // Waitlist Form Handler
    const waitlistForm = document.getElementById('waitlist-form');
    const formStatus = document.getElementById('form-status');
    const GOOGLE_SCRIPT_URL = 'https://script.google.com/macros/s/AKfycbxKVLIv9OxIthnYEZJszEypxSeTMLGNX6me_neaJLQz-pUX53nsmTaz8vk0PCKkSkRadw/exec';
    const WAITLIST_KEY = 'axorith_waitlist_joined';

    // Check if user already joined waitlist
    if (waitlistForm && localStorage.getItem(WAITLIST_KEY)) {
        waitlistForm.innerHTML = `
            <div class="success-message">
                <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                    <polyline points="22 4 12 14.01 9 11.01"/>
                </svg>
                <p>You're on the list!<br><span>See you on January 9th.</span></p>
            </div>
        `;
    } else if (waitlistForm) {
        waitlistForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            
            const submitBtn = waitlistForm.querySelector('button[type="submit"]');
            const emailInput = waitlistForm.querySelector('input[name="email"]');
            const email = emailInput.value.trim();

            // Basic email validation
            if (!email || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
                showStatus('Please enter a valid email address', 'error');
                emailInput.focus();
                return;
            }

            // Set loading state
            submitBtn.disabled = true;
            submitBtn.classList.add('loading');
            formStatus.textContent = '';
            formStatus.className = 'form-status';

            try {
                const formData = new FormData();
                formData.append('email', email);

                const response = await fetch(GOOGLE_SCRIPT_URL, {
                    method: 'POST',
                    body: formData
                });

                const data = await response.json();

                if (data.result === 'success') {
                    // Save to localStorage
                    localStorage.setItem(WAITLIST_KEY, email);
                    
                    // Success state
                    waitlistForm.classList.add('success');
                    showStatus("You're on the list! We'll notify you on January 9th.", 'success');
                    emailInput.value = '';
                    
                    // Hide form after success
                    setTimeout(() => {
                        waitlistForm.innerHTML = `
                            <div class="success-message">
                                <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                    <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                                    <polyline points="22 4 12 14.01 9 11.01"/>
                                </svg>
                                <p>You're on the list!<br><span>See you on January 9th.</span></p>
                            </div>
                        `;
                    }, 1500);
                } else {
                    showStatus(data.message || 'Something went wrong. Please try again.', 'error');
                    submitBtn.disabled = false;
                    submitBtn.classList.remove('loading');
                }
            } catch (error) {
                console.error('Waitlist form error:', error);
                showStatus('Connection error. Please try again.', 'error');
                submitBtn.disabled = false;
                submitBtn.classList.remove('loading');
            }
        });
    }

    function showStatus(message, type) {
        const formStatus = document.getElementById('form-status');
        if (formStatus) {
            formStatus.textContent = message;
            formStatus.className = `form-status ${type}`;
        }
    }
});