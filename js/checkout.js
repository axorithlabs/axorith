// Checkout Page Logic

const WALLET_ADDRESSES = {
    'usdt-trc20': 'TWT4pN3HJz2yUCNsVDfFbsp9EnaVnmZq3n',
    'usdt-erc20': '0xB1Aec06166336CF717B244DBDC8620820824F1D7',
    'usdc-erc20': '0xB1Aec06166336CF717B244DBDC8620820824F1D7',
    'btc': 'bc1q4p93zvy3pxdf293hwka2tmdyhr53zwju3xmwlf',
    'eth': '0xB1Aec06166336CF717B244DBDC8620820824F1D7',
    'sol': 'Ctuthbpvw2y7Q8fpjET8iqhw5UukcVUWTbuM3TT85KhH',
    'bnb': '0xB1Aec06166336CF717B244DBDC8620820824F1D7',
    'ltc': 'ltc1qgecn35js80n7yacyl5fw5pdsykamnzw4wf2wdl'
};

// Coin display names
const COIN_NAMES = {
    'usdt-trc20': 'USDT',
    'usdt-erc20': 'USDT',
    'usdc-erc20': 'USDC',
    'btc': 'BTC',
    'eth': 'ETH',
    'sol': 'SOL',
    'bnb': 'BNB',
    'ltc': 'LTC'
};

// Amount display (for stablecoins show USD equivalent)
const AMOUNTS = {
    'usdt-trc20': '99 USDT',
    'usdt-erc20': '99 USDT',
    'usdc-erc20': '99 USDC',
    'btc': '≈ $99 in BTC',
    'eth': '≈ $99 in ETH',
    'sol': '≈ $99 in SOL',
    'bnb': '≈ $99 in BNB',
    'ltc': '≈ $99 in LTC'
};

let selectedCoin = null;
let selectedNetwork = null;

document.addEventListener('DOMContentLoaded', function() {
    // Check if sales are enabled (SALES_ENABLED is defined in checkout.html)
    if (typeof SALES_ENABLED !== 'undefined' && !SALES_ENABLED) {
        showSalesDisabled();
        return;
    }
    
    // Coin selection
    const coinOptions = document.querySelectorAll('.coin-option');
    
    coinOptions.forEach(option => {
        option.addEventListener('click', function() {
            // Remove selected from all
            coinOptions.forEach(opt => opt.classList.remove('selected'));
            
            // Add selected to clicked
            this.classList.add('selected');
            
            // Store selection
            selectedCoin = this.dataset.coin;
            selectedNetwork = this.dataset.network;
            
            // Go to step 2 after short delay
            setTimeout(() => goToStep(2), 300);
        });
    });

    // Form submission
    const form = document.getElementById('confirmation-form');
    if (form) {
        form.addEventListener('submit', function(e) {
            e.preventDefault();
            
            const formData = new FormData(form);
            const data = {
                email: formData.get('email'),
                txHash: formData.get('txHash'),
                discord: formData.get('discord'),
                coin: selectedCoin,
                network: selectedNetwork
            };

            emailjs.send('service_6ll9f8g', 'template_aznxs4h', data)
            .then(function() {
                console.log('SUCCESS!');
                showSuccess();
            }, function(error) {
                console.log('FAILED...', error);
            });
        });
    }
});

function showSalesDisabled() {
    const wrapper = document.querySelector('.checkout-wrapper');
    if (wrapper) {
        wrapper.innerHTML = `
            <div class="sales-disabled">
                <div class="disabled-icon">
                    <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <circle cx="12" cy="12" r="10"/>
                        <polyline points="12 6 12 12 16 14"/>
                    </svg>
                </div>
                <h1>Coming Soon</h1>
                <p>Founding Member sales will be available on <strong>January 9, 2026</strong></p>
                <p class="disabled-note">Join our Discord to get notified when sales go live!</p>
                <div class="disabled-actions">
                    <a href="https://discord.gg/axorith" class="btn btn-primary" target="_blank">
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor">
                            <path d="M20.317 4.3698a19.7913 19.7913 0 00-4.8851-1.5152.0741.0741 0 00-.0785.0371c-.211.3753-.4447.8648-.6083 1.2495-1.8447-.2762-3.68-.2762-5.4868 0-.1636-.3933-.4058-.8742-.6177-1.2495a.077.077 0 00-.0785-.037 19.7363 19.7363 0 00-4.8852 1.515.0699.0699 0 00-.0321.0277C.5334 9.0458-.319 13.5799.0992 18.0578a.0824.0824 0 00.0312.0561c2.0528 1.5076 4.0413 2.4228 5.9929 3.0294a.0777.0777 0 00.0842-.0276c.4616-.6304.8731-1.2952 1.226-1.9942a.076.076 0 00-.0416-.1057c-.6528-.2476-1.2743-.5495-1.8722-.8923a.077.077 0 01-.0076-.1277c.1258-.0943.2517-.1923.3718-.2914a.0743.0743 0 01.0776-.0105c3.9278 1.7933 8.18 1.7933 12.0614 0a.0739.0739 0 01.0785.0095c.1202.099.246.1981.3728.2924a.077.077 0 01-.0066.1276 12.2986 12.2986 0 01-1.873.8914.0766.0766 0 00-.0407.1067c.3604.698.7719 1.3628 1.225 1.9932a.076.076 0 00.0842.0286c1.961-.6067 3.9495-1.5219 6.0023-3.0294a.077.077 0 00.0313-.0552c.5004-5.177-.8382-9.6739-3.5485-13.6604a.061.061 0 00-.0312-.0286zM8.02 15.3312c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9555-2.4189 2.157-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.9555 2.4189-2.1569 2.4189zm7.9748 0c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9554-2.4189 2.1569-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.946 2.4189-2.1568 2.4189Z"/>
                        </svg>
                        Join Discord
                    </a>
                    <a href="index.html" class="btn btn-secondary">
                        Back to Home
                    </a>
                </div>
            </div>
        `;
    }
}

function goToStep(step) {
    // Hide all steps
    document.querySelectorAll('.checkout-step').forEach(s => s.classList.add('hidden'));
    
    // Show target step
    const targetStep = document.getElementById(`step-${step}`);
    if (targetStep) {
        targetStep.classList.remove('hidden');
    }
    
    // Update progress
    document.querySelectorAll('.progress-step').forEach((s, index) => {
        const stepNum = index + 1;
        s.classList.remove('active', 'completed');
        
        if (stepNum < step) {
            s.classList.add('completed');
        } else if (stepNum === step) {
            s.classList.add('active');
        }
    });
    
    // If going to step 2, update payment details
    if (step === 2 && selectedCoin) {
        updatePaymentDetails();
    }
    
    // Scroll to top
    window.scrollTo({ top: 0, behavior: 'smooth' });
}

function updatePaymentDetails() {
    const address = WALLET_ADDRESSES[selectedCoin];
    const coinName = COIN_NAMES[selectedCoin];
    const amount = AMOUNTS[selectedCoin];
    
    document.getElementById('wallet-address').textContent = address;
    document.getElementById('selected-coin-name').textContent = coinName;
    document.getElementById('payment-network').textContent = selectedNetwork;
    document.getElementById('payment-amount').textContent = amount;
    document.getElementById('warning-coin').textContent = coinName;
    document.getElementById('warning-network').textContent = selectedNetwork;
}

function copyAddress() {
    const address = document.getElementById('wallet-address').textContent;
    const btn = document.querySelector('.copy-btn');
    
    navigator.clipboard.writeText(address).then(() => {
        btn.classList.add('copied');
        btn.querySelector('span').textContent = 'Copied!';
        
        setTimeout(() => {
            btn.classList.remove('copied');
            btn.querySelector('span').textContent = 'Copy';
        }, 2000);
    });
}

function showSuccess() {
    document.querySelectorAll('.checkout-step').forEach(s => s.classList.add('hidden'));
    document.getElementById('step-success').classList.remove('hidden');
    
    // Update all progress steps to completed
    document.querySelectorAll('.progress-step').forEach(s => {
        s.classList.remove('active');
        s.classList.add('completed');
    });
}